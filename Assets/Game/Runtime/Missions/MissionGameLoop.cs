using System.Collections;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
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
        private static MissionGameLoop instance;

        private LevelConfiguration definition;
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

        public MissionRun CurrentRun => run;
        public int Credits => PlayerEconomy.Credits;
        /// <summary>
        /// The active mission loop, or null. Scene-owned mission interactions use
        /// this without holding their own serialized reference.
        /// </summary>
        public static MissionGameLoop Instance => instance;
        public static bool IsSceneTransitioning =>
            instance != null && instance.transitioning;
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
            DontDestroyOnLoad(gameObject);
            Application.runInBackground = true;
            definition = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Missions.DefaultLevel
                : null;
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
            SceneManager.sceneLoaded += HandleSceneLoaded;
            EnsureUi();
        }

        private void OnDestroy()
        {
            if (instance != this) return;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            instance = null;
        }

        private void Update()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F1))
            {
                PlayerEconomy.AddCredits(DebugCreditGrant);
                SetPrompt(
                    "DEBUG +$" + DebugCreditGrant
                    + "    BALANCE: $" + Credits);
            }
#endif

            Scene activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.handle != configuredSceneHandle)
                ConfigureScene(activeScene);
            if (!caveSetup && activeScene.name == CaveSceneName)
                TrySetupCave();

            if (run != null && !run.IsFinished && caveSetup && !transitioning)
            {
                run.Tick(Time.deltaTime);
                RefreshObjective();
                if (run.IsFinished) ShowResult();
            }

            if (missionUi != null && missionUi.IsResultVisible
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
            {
                ReturnHome();
            }
        }

        public bool BeginFirstMission()
        {
            return BeginLevel(definition);
        }

        public bool BeginLevel(LevelConfiguration level)
        {
            if (transitioning || level == null
                || !level.HasCompleteGenerationConfiguration)
            {
                return false;
            }

            definition = level;
            run = new MissionRun(
                definition.EvacuationCountdownSeconds,
                definition.RequiredFunds);
            InvalidateObjectiveCache();
            StartCoroutine(LoadWithFade(CaveSceneName));
            return true;
        }

        public void DeliverOre(int value)
        {
            run?.AddDeliveredValue(value);
            SetPrompt("COLLECTED  +" + Mathf.Max(0, value));
            RefreshObjective();
        }

        /// <summary>
        /// The value shown to the player as collected. Before evacuation this
        /// includes the extraction Cell's live overlap tally; once evacuation
        /// starts that tally has been banked into DeliveredValue, so only the
        /// fixed total is shown.
        /// </summary>
        private int DisplayedCollectedValue
        {
            get
            {
                if (run == null) return 0;
                if (run.IsEvacuationCountdownActive) return run.DeliveredValue;
                int extraction = extractionZone != null
                    ? extractionZone.CurrentStoredValue
                    : 0;
                return run.DeliveredValue + extraction;
            }
        }

        public bool RequestEvacuation()
        {
            if (run == null || run.IsFinished || transitioning) return false;

            int storedValue = extractionZone != null
                ? extractionZone.CurrentStoredValue
                : 0;
            if (!run.TryStartEvacuationCountdown(storedValue))
            {
                int total = DisplayedCollectedValue;
                if (run.IsEvacuationCountdownActive)
                {
                    SetPrompt("EVACUATION COUNTDOWN ALREADY ACTIVE");
                }
                else
                {
                    int missingValue = Mathf.Max(0, run.RequiredValue - total);
                    SetPrompt(
                        "RETURN LOCKED · NEED $" + missingValue
                        + " MORE    STORED $" + total
                        + " / $" + run.RequiredValue);
                }
                return false;
            }

            InvalidateObjectiveCache();
            SetPrompt(
                "EVACUATION INITIATED · RETURN IN "
                + FormatCountdown(run.TimeRemaining));
            RefreshObjective();
            return true;
        }

        public void ShowCellActionPrompt(bool home)
        {
            if (home)
            {
                SetPrompt("PRESS E AT CELL CONSOLE TO START MISSION");
                return;
            }

            if (run == null || run.IsFinished) return;
            if (run.IsEvacuationCountdownActive)
            {
                SetPrompt("EVACUATION COUNTDOWN ACTIVE");
                return;
            }

            int storedValue = DisplayedCollectedValue;
            SetPrompt(storedValue >= run.RequiredValue
                ? "PRESS E AT CELL CONSOLE TO BEGIN EVACUATION"
                : "RETURN LOCKED · STORED $" + storedValue
                    + " / $" + run.RequiredValue);
        }

        public void HideCellActionPrompt(bool home)
        {
            if (home)
            {
                SetPrompt("SHOP ONLINE    BALANCE: $" + Credits);
            }
            else if (run != null && run.IsEvacuationCountdownActive)
            {
                SetPrompt("EVACUATION COUNTDOWN ACTIVE");
            }
            else
            {
                SetPrompt("");
            }
        }

        public void NotifyStoredValueChanged(int value)
        {
            SetPrompt(
                "TOTAL STORED VALUE: $" + Mathf.Max(0, DisplayedCollectedValue));
            RefreshObjective();
            cellZone?.RefreshActionPrompt();
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
            missionUi.HideResult();
            caveSetup = false;
            extractionZone = null;
            cellZone = null;
            if (scene.name == HomeSceneName) SetupHome();
            else if (scene.name == CaveSceneName)
            {
                EnsureRunForDirectCaveEntry();
                gameUi?.HideMissionTimer();
                SetPrompt("");
            }
        }

        private void EnsureRunForDirectCaveEntry()
        {
            if (run != null || definition == null)
                return;

            run = new MissionRun(
                definition.EvacuationCountdownSeconds,
                definition.RequiredFunds);
            InvalidateObjectiveCache();
        }

        private void SetupHome()
        {
            CreateCellTrigger(FindCell(), true);
            gameUi?.HideMissionTimer();
            missionUi.SetObjective(
                "SHIP BASE\n");
            missionUi.SetPrompt(
                "SHOP ONLINE    BALANCE: $" + Credits);
        }

        private void TrySetupCave()
        {
            VoxelPlayerController player = FindObjectOfType<VoxelPlayerController>();
            MinecraftCaveInfiniteWorld world = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            if (player == null || world == null || !world.IsInitialLoadComplete) return;

            CreateCellTrigger(FindCell(), false);
            if (!world.UsesExternalDenseLandingCell)
            {
                Vector3 cartPosition;
                Quaternion cartRotation;
                SpawnPointSceneStructure spawnStructure =
                    FindObjectOfType<SpawnPointSceneStructure>();
                if (spawnStructure != null)
                {
                    spawnStructure.GetMissionCartSpawnPose(
                        out cartPosition,
                        out cartRotation);
                }
                else
                {
                    cartPosition = player.transform.position
                        + player.transform.right * 2.2f
                        + player.transform.forward * 1.5f
                        + Vector3.up * 0.5f;
                    cartRotation = player.transform.rotation;
                }

                string authoredCartName = GameAssetCatalog.Current != null
                    ? GameAssetCatalog.Current.SceneLookups.AuthoredCartObjectName
                    : string.Empty;
                GameObject authoredCart = string.IsNullOrWhiteSpace(authoredCartName)
                    ? null
                    : GameObject.Find(authoredCartName);
                MissionCart.ConfigureExisting(
                    authoredCart,
                    cartPosition,
                    cartRotation);
            }
            ProximitySlidingDoor[] levelDoors =
                FindObjectsOfType<ProximitySlidingDoor>(true);
            for (int i = 0; i < levelDoors.Length; i++)
            {
                levelDoors[i].SetStayOpenAfterFirstOpen(true);
            }
            caveSetup = true;
            RefreshObjective();
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
            MissionCellButton.Create(cell, home);
            if (!home)
            {
                extractionZone = triggerObject.AddComponent<OreExtractionZone>();
                extractionZone.Configure(this);
            }
        }

        private static void FitTriggerToCellRenderers(
            Transform cell,
            BoxCollider trigger)
        {
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
                Bounds worldBounds = renderers[rendererIndex].bounds;
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
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            string message;
            switch (run.Outcome)
            {
                case MissionOutcome.Success:
                    int reward = run.ExcessValue;
                    PlayerEconomy.AddCredits(reward);
                    message = "MISSION COMPLETE"
                        + "\n\nCOLLECTED $" + run.DeliveredValue
                        + "\nBALANCE INCREASED $" + reward
                        + "\n\nPRESS ENTER TO RETURN";
                    break;
                case MissionOutcome.LostInCaves:
                    message = "MISSION FAILED"
                        + "\n\nEVACUATION WINDOW CLOSED."
                        + "\nYOU ARE LOST IN THE CAVES."
                        + "\n\nPRESS ENTER TO RETURN";
                    break;
                default:
                    message = "MISSION FAILED"
                        + "\n\nINSUFFICIENT RESOURCES COLLECTED"
                        + "\nCOLLECTED $" + run.DeliveredValue
                        + " / $" + run.RequiredValue
                        + "\n\nPRESS ENTER TO RETURN";
                    break;
            }
            missionUi.ShowResult(message);
        }

        private void ReturnHome()
        {
            if (transitioning) return;
            Time.timeScale = 1f;
            StartCoroutine(LoadWithFade(HomeSceneName));
        }

        private IEnumerator LoadWithFade(string sceneName)
        {
            EnsureUi();
            CanvasGroup fade = missionUi.SceneFade;
            transitioning = true;
            try
            {
                if (fade != null)
                {
                    if (!fade.gameObject.activeSelf)
                        fade.alpha = 0f;
                    fade.gameObject.SetActive(true);
                    fade.blocksRaycasts = true;
                    Canvas.ForceUpdateCanvases();
                    yield return null;
                    yield return FadeTo(
                        fade,
                        1f,
                        missionUi.FadeOutSeconds);
                }

                yield return new WaitForEndOfFrame();
                AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
                while (operation != null && !operation.isDone)
                    yield return null;

                EnsureUi();
                CanvasGroup activeFade = missionUi.SceneFade;
                if (activeFade != null)
                {
                    if (activeFade != fade)
                    {
                        activeFade.alpha = 1f;
                        activeFade.gameObject.SetActive(true);
                    }
                    fade = activeFade;
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
            if (!run.IsEvacuationCountdownActive)
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
            missionUi.SetObjective("LEVEL 01 · " + missionName
                + "\nCOLLECTED  $"
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

        private string MissionName => definition != null
            ? definition.DisplayName
            : string.Empty;

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
    }
}
