using System.Collections;
using Supernova.Audio;
using Supernova.Inputs;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.PortalExample;
using Supernova.Shop;
using Supernova.UI;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Supernova.Missions
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class MissionGameLoop : MonoBehaviour
    {
        private const int DebugCreditGrant = 100;
        private const float CaveAmbienceVolumeScale = 0.01f;
        private const float TransitionSoundVolumeScale = 0.6f;
        private const float DefaultResultCountDurationSeconds = 2f;
        private const float EarlyEvacuationHoldSeconds = 2f;
        private static MissionGameLoop instance;

        private LevelConfiguration definition;
        private MissionCampaignProgress campaign;
        private MissionRun run;
        private GameHudController gameUi;
        private MissionUiView missionUi;
        private bool transitioning;
        private bool caveSetup;
        private int configuredSceneHandle = int.MinValue;
        private OreExtractionZone extractionZone;
        private MissionCellZone cellZone;
        private int displayedObjectiveSeconds = int.MinValue;
        private int displayedObjectiveStoredValue = int.MinValue;
        private int displayedObjectiveRequiredValue = int.MinValue;
        private string displayedObjectiveMissionName;
        private int ambienceLoopId;
        private bool playReadyAfterCaveLoad;
        private bool enterHomeGameplayDirectly;
        private float earlyEvacuationHeldSeconds;
        private bool displayedEarlyEvacuationAvailable;
        private float displayedEarlyEvacuationProgress = -1f;

        public MissionRun CurrentRun => run;
        public int Credits => PlayerEconomy.Credits;
        public float EarlyEvacuationHoldDuration =>
            EarlyEvacuationHoldSeconds;
        public float EarlyEvacuationHoldProgress => Mathf.Clamp01(
            earlyEvacuationHeldSeconds / EarlyEvacuationHoldSeconds);
        public bool IsEarlyEvacuationAvailable =>
            run != null
            && !run.IsFinished
            && !transitioning
            && caveSetup
            && DisplayedCollectedValue >= run.RequiredValue;
        public bool CanBeginCurrentMission =>
            !transitioning
            && campaign != null
            && !campaign.IsComplete
            && definition != null
            && definition.HasCompleteGenerationConfiguration;
        /// <summary>
        /// The active mission loop, or null. Scene-owned mission interactions use
        /// this without holding their own serialized reference.
        /// </summary>
        public static MissionGameLoop Instance => instance;
        public static bool IsSceneTransitioning =>
            instance != null && instance.transitioning;
        public static bool ConsumeDirectHomeGameplayEntry()
        {
            if (instance == null || !instance.enterHomeGameplayDirectly)
                return false;

            instance.enterHomeGameplayDirectly = false;
            return true;
        }
        public static bool HasSavedCampaignProgress
        {
            get
            {
                MissionAssetReferences missions =
                    GameAssetCatalog.Current != null
                        ? GameAssetCatalog.Current.Missions
                        : null;
                return missions != null
                    && MissionProgressPersistence.TryLoadLevel(
                        missions.Levels,
                        out _);
            }
        }
        public static LevelConfiguration CurrentLevelConfiguration
        {
            get
            {
                if (instance != null && instance.definition != null)
                {
                    return instance.definition;
                }
                return GameAssetCatalog.Current != null
                    ? GameAssetCatalog.Current.Missions.DefaultLevel
                    : null;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject root = new GameObject("Mission Game Loop");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<MissionGameLoop>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            ambienceLoopId = SoundEffectEvents.CreateLoopId();
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            MissionAssetReferences missions = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Missions
                : null;
            definition = missions != null
                ? MissionProgressPersistence.ResolveSavedOrDefault(
                    missions.Levels,
                    missions.DefaultLevel)
                : null;
            campaign = new MissionCampaignProgress(
                missions != null ? missions.Levels : null,
                definition);
            if (campaign.CurrentLevel != null)
                definition = campaign.CurrentLevel;
            if (definition == null)
            {
                Debug.LogError(
                    "The preloaded game asset catalog does not provide a default level. "
                    + "The game loop cannot start without a level.");
            }
            else if (!definition.HasCompleteGenerationConfiguration)
            {
                Debug.LogError(
                    "The active level must combine world, monster, and treasure "
                    + "generation configurations.",
                    definition);
            }
            PlayerEconomy.CreditsChanged += HandleCreditsChanged;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureUi();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            StopCaveAmbience();
            PlayerEconomy.CreditsChanged -= HandleCreditsChanged;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (GameInput.Pressed(GameInputActionId.DebugMission))
            {
                PlayerEconomy.AddCredits(DebugCreditGrant);
            }
#endif

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.handle != configuredSceneHandle)
                ConfigureScene(activeScene);
            if (!caveSetup && activeScene.name == CaveSceneName)
                TrySetupCave();

            if (run != null && !run.IsFinished && caveSetup && !transitioning)
            {
                int storedValue = extractionZone != null
                    ? extractionZone.CurrentStoredValue
                    : 0;
                run.Tick(Time.deltaTime, storedValue);
                RefreshObjective();
                if (run.IsFinished)
                {
                    ResetEarlyEvacuationState();
                    ShowResult();
                }
                else
                {
                    TickEarlyEvacuationHold(
                        Time.deltaTime,
                        GameInput.Held(GameInputActionId.Interact),
                        GameHudController.IsGameplayInputBlocked);
                }
            }
            else
            {
                ResetEarlyEvacuationState();
            }

            if (missionUi != null && missionUi.IsResultVisible
                && GameInput.Pressed(GameInputActionId.Submit))
            {
                ReturnHome();
            }
        }

        public bool BeginFirstMission()
        {
            if (campaign != null && campaign.IsComplete)
                return false;
            return BeginLevel(definition);
        }

        public bool StartNewCampaign()
        {
            MissionAssetReferences missions = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Missions
                : null;
            LevelConfiguration firstLevel =
                missions != null ? missions.DefaultLevel : null;
            if (firstLevel == null
                || campaign == null
                || !campaign.SelectLevel(firstLevel))
            {
                return false;
            }

            definition = firstLevel;
            run = null;
            MissionProgressPersistence.ClearSavedProgress();
            PlayerEconomy.ClearSavedProgress();
            bool saved = MissionProgressPersistence.SaveCurrentLevel(definition);
            if (saved)
                NewGameGuideOverlay.MarkForNewCampaign();
            return saved;
        }

        public bool ContinueCampaign()
        {
            MissionAssetReferences missions = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Missions
                : null;
            if (missions == null
                || campaign == null
                || !MissionProgressPersistence.TryLoadLevel(
                    missions.Levels,
                    out LevelConfiguration savedLevel)
                || !campaign.SelectLevel(savedLevel))
            {
                return false;
            }

            definition = savedLevel;
            run = null;
            return true;
        }

        /// <summary>
        /// Fades the current scene to black, loads the configured destination,
        /// then fades the destination scene back in.
        /// </summary>
        public bool BeginSceneLoadWithFade(string sceneName)
        {
            if (transitioning
                || string.IsNullOrWhiteSpace(sceneName)
                || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return false;
            }

            StartCoroutine(LoadWithFadeInternal(sceneName, false));
            return true;
        }

        /// <summary>
        /// Loads a scene while the persistent mission overlay is already fully
        /// opaque, then fades the destination scene in after loading completes.
        /// </summary>
        public bool BeginSceneLoadFromBlack(string sceneName)
        {
            if (transitioning
                || string.IsNullOrWhiteSpace(sceneName)
                || !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                return false;
            }

            StartCoroutine(LoadWithFadeInternal(sceneName, true));
            return true;
        }

        /// <summary>
        /// Makes the persistent scene fade available to an external transition.
        /// The caller may drive its alpha before handing the scene load back to
        /// <see cref="BeginSceneLoadFromBlack"/>.
        /// </summary>
        public CanvasGroup PrepareSceneFadeFromTransparent()
        {
            if (transitioning)
                return null;

            EnsureUi();
            CanvasGroup fade = missionUi != null
                ? missionUi.SceneFade
                : null;
            if (fade == null)
                return null;

            PresentSceneFade(fade, 0f);
            return fade;
        }

        public bool BeginLevel(LevelConfiguration level)
        {
            if (transitioning || level == null
                || !level.HasCompleteGenerationConfiguration)
            {
                return false;
            }

            definition = level;
            bool selectedCampaignLevel =
                campaign != null && campaign.SelectLevel(level);
            if (campaign == null || selectedCampaignLevel)
                MissionProgressPersistence.SaveCurrentLevel(level);
            run = new MissionRun(
                definition.MissionTimeLimitSeconds,
                definition.RequiredFunds);
            InvalidateObjectiveCache();
            playReadyAfterCaveLoad =
                SceneManager.GetActiveScene().name == HomeSceneName;
            if (playReadyAfterCaveLoad)
            {
                RequestSound(
                    AudioAssets != null
                        ? AudioAssets.MissionStart
                        : null,
                    transform.position,
                    TransitionSoundVolumeScale);
            }
            StartCoroutine(LoadWithFade(CaveSceneName));
            return true;
        }

        public void DeliverOre(int value)
        {
            run?.AddDeliveredValue(value);
            RefreshObjective();
            RefreshEarlyEvacuationAvailability();
        }

        /// <summary>
        /// The value shown to the player as collected. While the mission is
        /// active this includes the extraction Cell's live overlap tally. At
        /// timeout that tally is banked into DeliveredValue for final scoring.
        /// </summary>
        private int DisplayedCollectedValue
        {
            get
            {
                if (run == null) return 0;
                if (run.IsFinished) return run.DeliveredValue;
                int extraction = extractionZone != null
                    ? extractionZone.CurrentStoredValue
                    : 0;
                return run.DeliveredValue + extraction;
            }
        }

        public bool RequestEvacuation()
        {
            if (!IsEarlyEvacuationAvailable)
                return false;

            int storedValue = extractionZone != null
                ? extractionZone.CurrentStoredValue
                : 0;
            if (!run.TryEvacuateEarly(storedValue))
                return false;

            ResetEarlyEvacuationState();
            ShowResult();
            return true;
        }

        private void TickEarlyEvacuationHold(
            float deltaTime,
            bool interactHeld,
            bool gameplayInputBlocked)
        {
            if (!IsEarlyEvacuationAvailable)
            {
                ResetEarlyEvacuationState();
                return;
            }

            bool canContinueHolding = interactHeld
                && !gameplayInputBlocked;
            earlyEvacuationHeldSeconds = canContinueHolding
                ? Mathf.Min(
                    EarlyEvacuationHoldSeconds,
                    earlyEvacuationHeldSeconds
                        + Mathf.Max(0f, deltaTime))
                : 0f;
            float progress = EarlyEvacuationHoldProgress;
            PublishEarlyEvacuationState(true, progress);
            if (progress >= 1f)
                RequestEvacuation();
        }

        private void RefreshEarlyEvacuationAvailability()
        {
            if (!IsEarlyEvacuationAvailable)
            {
                ResetEarlyEvacuationState();
                return;
            }

            PublishEarlyEvacuationState(
                true,
                EarlyEvacuationHoldProgress);
        }

        private void ResetEarlyEvacuationState()
        {
            earlyEvacuationHeldSeconds = 0f;
            PublishEarlyEvacuationState(false, 0f);
        }

        private void PublishEarlyEvacuationState(
            bool available,
            float progress)
        {
            float clampedProgress = Mathf.Clamp01(progress);
            if (displayedEarlyEvacuationAvailable == available
                && Mathf.Approximately(
                    displayedEarlyEvacuationProgress,
                    clampedProgress))
            {
                return;
            }

            displayedEarlyEvacuationAvailable = available;
            displayedEarlyEvacuationProgress = clampedProgress;
            if (missionUi == null)
                EnsureUi();
            missionUi?.SetEarlyEvacuationState(
                available,
                clampedProgress);
        }

        public void ShowCellActionPrompt(bool home)
        {
            if (!home)
                return;

            SetPrompt(campaign != null && campaign.IsComplete
                ? string.Empty
                : "按 {{input:Gameplay/Interact}} 开始任务");
        }

        public void HideCellActionPrompt(bool home)
        {
            // Proximity prompts were removed from the lower-middle HUD.
        }

        public void ShowTutorialExitPrompt()
        {
            SetPrompt("按 {{input:Gameplay/Interact}} 结束教程");
        }

        public void HideTutorialExitPrompt()
        {
            SetPrompt(string.Empty);
        }

        public bool EndTutorial()
        {
            if (transitioning
                || SceneManager.GetActiveScene().name != TutorialSceneName)
            {
                return false;
            }

            string mainMenuSceneName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MainMenuSceneName
                : string.Empty;
            if (!BeginSceneLoadWithFade(mainMenuSceneName))
                return false;

            HideTutorialExitPrompt();
            return true;
        }

        public void NotifyStoredValueChanged(int value)
        {
            RefreshObjective();
            RefreshEarlyEvacuationAvailability();
        }

        public void NotifyStoredResourceAdded(
            int value,
            Vector3 _)
        {
            if (value <= 0) return;

            RequestSound(
                AudioAssets != null ? AudioAssets.CoinDeposit : null,
                ResolvePlayerSoundPosition(),
                1f);
        }

        private Vector3 ResolvePlayerSoundPosition()
        {
            VoxelPlayerController player =
                FindObjectOfType<VoxelPlayerController>();
            return player != null
                ? player.transform.position
                : transform.position;
        }


        public void SetPrompt(string message)
        {
            EnsureUi();
            missionUi?.SetPrompt(message);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ConfigureScene(scene);
        }

        private void ConfigureScene(Scene scene)
        {
            if (!scene.IsValid() || scene.handle == configuredSceneHandle) return;
            configuredSceneHandle = scene.handle;
            InvalidateObjectiveCache();
            EnsureUi();
            ResetEarlyEvacuationState();
            missionUi.HideResult();
            missionUi.SetPrompt(string.Empty);
            if (scene.name != HomeSceneName)
                missionUi.SetObjective(string.Empty);
            caveSetup = false;
            extractionZone = null;
            cellZone = null;
            if (scene.name == HomeSceneName)
            {
                StopCaveAmbience();
                playReadyAfterCaveLoad = false;
                SetupHome();
            }
            else if (scene.name == CaveSceneName)
            {
                StartCaveAmbience();
                EnsureRunForDirectCaveEntry();
                gameUi?.HideMissionTimer();
                RefreshEarlyEvacuationAvailability();
            }
            else if (scene.name == TutorialSceneName)
            {
                StopCaveAmbience();
                playReadyAfterCaveLoad = false;
                gameUi?.HideMissionTimer();
                CreateTutorialExitTrigger(FindCell());
            }
            else
            {
                StopCaveAmbience();
                playReadyAfterCaveLoad = false;
            }
        }

        private void EnsureRunForDirectCaveEntry()
        {
            if (run != null || definition == null)
                return;

            run = new MissionRun(
                definition.MissionTimeLimitSeconds,
                definition.RequiredFunds);
            InvalidateObjectiveCache();
        }

        private void SetupHome()
        {
            CreateCellTrigger(FindCell(), true);
            gameUi?.HideMissionTimer();
            RefreshHomeObjective();
        }

        private void HandleCreditsChanged(int _)
        {
            if (SceneManager.GetActiveScene().name == HomeSceneName)
                RefreshHomeObjective();
        }

        private void RefreshHomeObjective()
        {
            missionUi?.SetObjective(FormatHomeObjective(Credits));
        }

        private static string FormatHomeObjective(int credits)
        {
            return "基地\n当前存款  $" + Mathf.Max(0, credits);
        }

        private void TrySetupCave()
        {
            VoxelPlayerController player = FindObjectOfType<VoxelPlayerController>();
            MinecraftCaveInfiniteWorld world = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            if (player == null || world == null || !world.IsInitialLoadComplete) return;

            CreateCellTrigger(FindCell(), false);
            ProximitySlidingDoor[] levelDoors =
                FindObjectsOfType<ProximitySlidingDoor>(true);
            for (int i = 0; i < levelDoors.Length; i++)
            {
                levelDoors[i].SetStayOpenAfterFirstOpen(true);
            }
            caveSetup = true;
            if (playReadyAfterCaveLoad)
            {
                RequestSound(
                    AudioAssets != null
                        ? AudioAssets.MissionReady
                        : null,
                    transform.position,
                    TransitionSoundVolumeScale);
                playReadyAfterCaveLoad = false;
            }
            RefreshObjective();
            if (NewGameGuideOverlay.IsPendingForCurrentCampaign
                && !NewGameGuideOverlay.TryShow(gameUi))
            {
                Debug.LogError(
                    "The new-game guide could not be shown when the first mission "
                    + "became ready.",
                    this);
            }
        }

        private void CreateCellTrigger(Transform cell, bool home)
        {
            if (cell == null)
            {
                GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                marker.name = home ? "Home Cell" : "Extraction Cell";
                marker.transform.position = Vector3.zero;
                marker.transform.localScale = new Vector3(2.5f, 0.1f, 2.5f);
                cell = marker.transform;
            }

            GameObject triggerObject = new GameObject(
                home ? "Mission Launch Trigger" : "Mission Extraction Trigger");
            triggerObject.transform.SetParent(cell, false);
            triggerObject.transform.localPosition = Vector3.up;
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5f, 3f, 5f);
            FitTriggerToCellRenderers(cell, trigger);
            MissionCellZone zone = triggerObject.AddComponent<MissionCellZone>();
            zone.Configure(this, home);
            cellZone = zone;
            if (!home)
                MissionCellButton.Create(cell, false);
            if (!home)
            {
                extractionZone = triggerObject.AddComponent<OreExtractionZone>();
                extractionZone.Configure(this);
            }
        }

        private void CreateTutorialExitTrigger(Transform cell)
        {
            if (cell == null)
            {
                Debug.LogError(
                    "The tutorial Cell could not be found, so its exit zone "
                    + "was not created.",
                    this);
                return;
            }

            GameObject triggerObject = new GameObject("Tutorial Exit Trigger");
            triggerObject.transform.SetParent(cell, false);
            triggerObject.transform.localPosition = Vector3.up;
            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(5f, 3f, 5f);
            FitTriggerToCellRenderers(cell, trigger);

            MissionCellZone zone = triggerObject.AddComponent<MissionCellZone>();
            zone.ConfigureTutorialExit(this);
            cellZone = zone;
        }

        private static void FitTriggerToCellRenderers(
            Transform cell,
            BoxCollider trigger)
        {
            SpawnPointSceneStructure spawnStructure =
                cell.GetComponent<SpawnPointSceneStructure>();
            if (spawnStructure != null)
            {
                Bounds extractionBounds =
                    spawnStructure.MissionExtractionLocalBounds;
                trigger.transform.localPosition = Vector3.zero;
                trigger.center = extractionBounds.center;
                trigger.size = extractionBounds.size;
                return;
            }

            Renderer[] renderers = cell.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                return;
            }

            bool hasBounds = false;
            Bounds localBounds = default;
            for (int rendererIndex = 0;
                rendererIndex < renderers.Length;
                rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                if (renderer.GetComponentInParent<PortalExampleGate>(true)
                    != null)
                {
                    continue;
                }

                Bounds worldBounds = renderer.bounds;
                Vector3 minimum = worldBounds.min;
                Vector3 maximum = worldBounds.max;
                for (int x = 0; x <= 1; x++)
                {
                    for (int y = 0; y <= 1; y++)
                    {
                        for (int z = 0; z <= 1; z++)
                        {
                            Vector3 worldCorner = new Vector3(
                                x == 0 ? minimum.x : maximum.x,
                                y == 0 ? minimum.y : maximum.y,
                                z == 0 ? minimum.z : maximum.z);
                            Vector3 localCorner =
                                cell.InverseTransformPoint(worldCorner);
                            if (!hasBounds)
                            {
                                localBounds = new Bounds(
                                    localCorner,
                                    Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                localBounds.Encapsulate(localCorner);
                            }
                        }
                    }
                }
            }

            if (!hasBounds)
            {
                return;
            }

            trigger.transform.localPosition = Vector3.zero;
            trigger.center = localBounds.center;
            trigger.size = localBounds.size + Vector3.one * 0.1f;
        }

        private static Transform FindCell()
        {
            string cellName = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.SceneLookups.MissionCellObjectName
                : string.Empty;
            GameObject exact = string.IsNullOrWhiteSpace(cellName)
                ? null
                : GameObject.Find(cellName);
            if (exact != null) return exact.transform;
            foreach (Transform candidate in FindObjectsOfType<Transform>())
            {
                if (!string.IsNullOrWhiteSpace(cellName)
                    && candidate.name.IndexOf(
                        cellName,
                        System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return candidate;
            }
            return null;
        }

        private void ShowResult()
        {
            EnsureUi();
            if (run == null || missionUi.IsResultVisible) return;
            ResetEarlyEvacuationState();
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            string message;
            switch (run.Outcome)
            {
                case MissionOutcome.Success:
                    int reward = run.ExcessValue;
                    PlayerEconomy.AddCredits(reward);
                    bool advanced = campaign != null
                        && campaign.RecordOutcome(run.Outcome);
                    if (advanced)
                    {
                        definition = campaign.CurrentLevel;
                        MissionProgressPersistence.SaveCurrentLevel(definition);
                    }
                    message = FormatResultMessage(
                        run.Outcome,
                        run.DeliveredValue,
                        run.RequiredValue,
                        reward,
                        advanced,
                        LevelNumberText);
                    break;
                case MissionOutcome.LostInCaves:
                    message = FormatResultMessage(
                        run.Outcome,
                        run.DeliveredValue,
                        run.RequiredValue,
                        0,
                        false,
                        LevelNumberText);
                    break;
                default:
                    message = FormatResultMessage(
                        run.Outcome,
                        run.DeliveredValue,
                        run.RequiredValue,
                        0,
                        false,
                        LevelNumberText);
                    break;
            }
            ShowAnimatedResult(message);
        }

        private static string FormatResultMessage(
            MissionOutcome outcome,
            int deliveredValue,
            int requiredValue,
            int reward,
            bool advanced,
            string nextLevelNumber)
        {
            switch (outcome)
            {
                case MissionOutcome.Success:
                    return "任务完成"
                        + "\n\n已收集：$" + Mathf.Max(0, deliveredValue)
                        + "\n存款增加：$" + Mathf.Max(0, reward)
                        + (advanced
                            ? "\n下一关：第" + nextLevelNumber + "关"
                            : "\n所有关卡已完成")
                        + "\n\n按 {{input:UI/Submit}} 返回基地";
                case MissionOutcome.LostInCaves:
                    return "任务失败"
                        + "\n\n撤离窗口已关闭。"
                        + "\n你被困在洞穴中。"
                        + "\n\n按 {{input:UI/Submit}} 返回基地";
                default:
                    return "任务失败"
                        + "\n\n收集的资源不足"
                        + "\n已收集：$" + Mathf.Max(0, deliveredValue)
                        + " / $" + Mathf.Max(1, requiredValue)
                        + "\n\n按 {{input:UI/Submit}} 返回基地";
            }
        }

        private void ShowAnimatedResult(string message)
        {
            string collectedToken = "$" + run.DeliveredValue;
            int tokenIndex = message.IndexOf(
                collectedToken,
                System.StringComparison.Ordinal);
            if (tokenIndex < 0)
            {
                missionUi.ShowResult(message);
                return;
            }

            string prefix = message.Substring(0, tokenIndex + 1);
            string suffix = message.Substring(
                tokenIndex + collectedToken.Length);
            SoundEffectCue cashGrowing = AudioAssets != null
                ? AudioAssets.CashGrowing
                : null;
            RequestSound(cashGrowing, transform.position, 1f);
            float duration = cashGrowing != null
                ? cashGrowing.MaximumClipLength
                : DefaultResultCountDurationSeconds;
            missionUi.ShowResultCountAnimation(
                prefix,
                suffix,
                run.DeliveredValue,
                Mathf.Max(0.1f, duration));
        }

        private void ReturnHome()
        {
            if (transitioning) return;
            Time.timeScale = 1f;
            enterHomeGameplayDirectly = true;
            StartCoroutine(LoadWithFade(HomeSceneName));
        }

        private IEnumerator LoadWithFade(string sceneName)
        {
            return LoadWithFadeInternal(sceneName, false);
        }

        private IEnumerator LoadWithFadeInternal(
            string sceneName,
            bool beginFullyBlack)
        {
            EnsureUi();
            CanvasGroup fade = missionUi.SceneFade;
            transitioning = true;
            try
            {
                if (fade != null)
                {
                    if (beginFullyBlack)
                        fade.alpha = 1f;
                    else if (!fade.gameObject.activeSelf)
                        fade.alpha = 0f;
                    PresentSceneFade(fade, fade.alpha);
                    yield return null;
                    if (!beginFullyBlack)
                    {
                        yield return FadeTo(
                            fade,
                            1f,
                            missionUi.FadeOutSeconds);
                    }
                }

                yield return new WaitForEndOfFrame();
                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
                while (operation != null && !operation.isDone)
                    yield return null;

                EnsureUi();
                CanvasGroup activeFade = missionUi.SceneFade;
                if (activeFade != null)
                {
                    fade = activeFade;
                    PresentSceneFade(fade, 1f);
                    yield return null;
                    yield return FadeTo(
                        fade,
                        0f,
                        missionUi.FadeInSeconds);
                }
            }
            finally
            {
                if (fade != null)
                    fade.gameObject.SetActive(false);
                transitioning = false;
            }
        }

        private static void PresentSceneFade(CanvasGroup fade, float alpha)
        {
            if (fade == null)
                return;

            Canvas[] overlayCanvases = fade.GetComponentsInParent<Canvas>(true);
            for (int i = 0; i < overlayCanvases.Length; i++)
            {
                if (overlayCanvases[i] != null)
                    overlayCanvases[i].gameObject.SetActive(true);
            }
            fade.alpha = Mathf.Clamp01(alpha);
            fade.gameObject.SetActive(true);
            fade.blocksRaycasts = true;
            Canvas.ForceUpdateCanvases();
        }

        private static IEnumerator FadeTo(
            CanvasGroup fade,
            float target,
            float duration)
        {
            float start = fade.alpha;
            float elapsed = 0f;
            while (fade != null && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fade.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            if (fade != null)
                fade.alpha = target;
        }

        private void RefreshObjective()
        {
            if (run == null) return;
            EnsureUi();
            if (missionUi == null) return;
            int seconds = Mathf.CeilToInt(run.TimeRemaining);
            if (!run.IsCountdownActive)
            {
                gameUi?.HideMissionTimer();
            }
            else if (seconds != displayedObjectiveSeconds)
            {
                gameUi?.SetMissionTimeRemaining(run.TimeRemaining);
            }
            int storedValue = DisplayedCollectedValue;
            int requiredValue = run.RequiredValue;
            string missionName = MissionName;
            if (seconds == displayedObjectiveSeconds
                && storedValue == displayedObjectiveStoredValue
                && requiredValue == displayedObjectiveRequiredValue
                && missionName == displayedObjectiveMissionName)
            {
                return;
            }

            displayedObjectiveSeconds = seconds;
            displayedObjectiveStoredValue = storedValue;
            displayedObjectiveRequiredValue = requiredValue;
            displayedObjectiveMissionName = missionName;
            missionUi.SetObjective(
                "第 " + LevelNumberText + " 关 · " + missionName
                + "\n已收集  $"
                + storedValue
                + " / $" + requiredValue);
        }

        private void InvalidateObjectiveCache()
        {
            displayedObjectiveSeconds = int.MinValue;
            displayedObjectiveStoredValue = int.MinValue;
            displayedObjectiveRequiredValue = int.MinValue;
            displayedObjectiveMissionName = null;
        }

        private void EnsureUi()
        {
            if (missionUi != null) return;

            gameUi = GetComponentInChildren<GameHudController>(true);
            if (gameUi == null)
            {
                GameObject gameUiObject = new GameObject("Game UI");
                gameUiObject.transform.SetParent(transform, false);
                gameUi = gameUiObject.AddComponent<GameHudController>();
            }

            gameUi.RegisterAsRuntimeHud();
            missionUi = gameUi.GetOrCreateMissionView();
            if (missionUi == null)
            {
                Debug.LogError(
                    "The unified game UI did not provide its mission view.",
                    gameUi);
            }
        }

        private void StartCaveAmbience()
        {
            if (ambienceLoopId == 0)
                ambienceLoopId = SoundEffectEvents.CreateLoopId();

            SoundEffectEvents.RequestLoop(
                ambienceLoopId,
                AudioAssets != null ? AudioAssets.CaveAmbience : null,
                transform,
                CaveAmbienceVolumeScale);
        }

        private void StopCaveAmbience()
        {
            if (ambienceLoopId != 0)
                SoundEffectEvents.RequestStopLoop(ambienceLoopId);
        }

        private static void RequestSound(
            SoundEffectCue cue,
            Vector3 worldPosition,
            float volumeScale)
        {
            SoundEffectEvents.RequestPlay(
                cue,
                worldPosition,
                volumeScale);
        }

        private static AudioAssetReferences AudioAssets =>
            GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Audio
                : null;

        private string MissionName => definition != null
            ? definition.DisplayName
            : string.Empty;
        private string LevelNumberText => definition != null
            ? definition.LevelNumber.ToString("00")
            : "--";

        private static string FormatCountdown(float secondsRemaining)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining));
            return (totalSeconds / 60).ToString("00")
                + ":" + (totalSeconds % 60).ToString("00");
        }

        private string CaveSceneName => definition != null
            ? definition.CaveSceneName
            : string.Empty;
        private string HomeSceneName => definition != null
            ? definition.HomeSceneName
            : string.Empty;
        private string TutorialSceneName => GameAssetCatalog.Current != null
            ? GameAssetCatalog.Current.SceneLookups.TutorialSceneName
            : string.Empty;
    }
}
