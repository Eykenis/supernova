using System.Collections;
using Supernova.Infrastructure;
using Supernova.MinecraftCaves;
using Supernova.Voxels;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Supernova.Missions
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class MissionGameLoop : MonoBehaviour
    {
        private const string CreditsKey = "Supernova.Credits";
        private const float SceneFadeOutSeconds = 0.65f;
        private const float SceneFadeInSeconds = 0.55f;
        private static MissionGameLoop instance;
        private static Font missionFont;

        private LevelConfiguration definition;
        private MissionRun run;
        private Canvas canvas;
        private CanvasGroup fade;
        private Text objectiveText;
        private Text promptText;
        private Text resultText;
        private GameObject resultPanel;
        private bool transitioning;
        private bool caveSetup;
        private int configuredSceneHandle = int.MinValue;
        private OreExtractionZone extractionZone;

        public MissionRun CurrentRun => run;
        public int Credits => PlayerPrefs.GetInt(CreditsKey, 0);
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

            if (resultPanel != null && resultPanel.activeSelf
                && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space)))
            {
                ReturnHome();
            }
        }

        public void BeginFirstMission()
        {
            BeginLevel(definition);
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
                definition.TimeLimitSeconds,
                definition.RequiredFunds);
            StartCoroutine(LoadWithFade(CaveSceneName));
            return true;
        }

        public void DeliverOre(int value)
        {
            run?.AddDeliveredValue(value);
            SetPrompt("资源已回收  +" + Mathf.Max(0, value));
            RefreshObjective();
        }

        public void RequestEvacuation()
        {
            if (run == null || run.IsFinished || transitioning) return;
            run.AddDeliveredValue(
                extractionZone != null ? extractionZone.CurrentStoredValue : 0);
            run.Evacuate();
            ShowResult();
        }

        public void NotifyStoredValueChanged(int value)
        {
            SetPrompt("CELL 仓储价值  $" + Mathf.Max(0, value));
            RefreshObjective();
        }

        public void SetPrompt(string message)
        {
            if (promptText != null) promptText.text = message;
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ConfigureScene(scene);
        }

        private void ConfigureScene(Scene scene)
        {
            if (!scene.IsValid() || scene.handle == configuredSceneHandle) return;
            configuredSceneHandle = scene.handle;
            EnsureUi();
            resultPanel.SetActive(false);
            caveSetup = false;
            extractionZone = null;
            if (scene.name == HomeSceneName) SetupHome();
            else if (scene.name == CaveSceneName)
                SetPrompt("正在部署 CELL 与矿车…");
        }

        private void SetupHome()
        {
            CreateCellTrigger(FindCell(), true);
            objectiveText.text = "HOME · 飞船基地\n进入 CELL 降落舱开始第一关";
            promptText.text = "商店（即将开放）    余额  $" + Credits;
        }

        private void TrySetupCave()
        {
            VoxelPlayerController player = FindObjectOfType<VoxelPlayerController>();
            MinecraftCaveInfiniteWorld world = FindObjectOfType<MinecraftCaveInfiniteWorld>();
            if (player == null || world == null || !world.IsInitialLoadComplete) return;

            CreateCellTrigger(FindCell(), false);
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
            ProximitySlidingDoor[] levelDoors =
                FindObjectsOfType<ProximitySlidingDoor>(true);
            for (int i = 0; i < levelDoors.Length; i++)
            {
                levelDoors[i].SetStayOpenAfterFirstOpen(true);
            }
            caveSetup = true;
            SetPrompt("用 MAGNET 搬运矿石 · 点击把手牵引矿车 · 回到 CELL 按 E 提前撤离");
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
            MissionCellZone zone = triggerObject.AddComponent<MissionCellZone>();
            zone.Configure(this, home);
            if (!home)
            {
                extractionZone = triggerObject.AddComponent<OreExtractionZone>();
                extractionZone.Configure(this, OreUnitValue);
            }
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
            if (run == null || resultPanel.activeSelf) return;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            resultPanel.SetActive(true);
            switch (run.Outcome)
            {
                case MissionOutcome.Success:
                    int reward = run.ExcessValue;
                    PlayerPrefs.SetInt(CreditsKey, Credits + reward);
                    PlayerPrefs.Save();
                    resultText.text = "任务完成\n\n已带回 $" + run.DeliveredValue
                        + "\n超额资源归你所有  +$" + reward
                        + "\n\n按 ENTER 返回 HOME";
                    break;
                case MissionOutcome.LostInCaves:
                    resultText.text = "LOST IN CAVES\n\n撤离窗口已经关闭。"
                        + "\n你迷失在矿洞深处。\n\n按 ENTER 返回 HOME";
                    break;
                default:
                    resultText.text = "任务失败\n\n你因没有开采足够的资源而被解雇了"
                        + "\n已带回 $" + run.DeliveredValue + " / $" + run.RequiredValue
                        + "\n\n按 ENTER 返回 HOME";
                    break;
            }
        }

        private void ReturnHome()
        {
            if (transitioning) return;
            Time.timeScale = 1f;
            StartCoroutine(LoadWithFade(HomeSceneName));
        }

        private IEnumerator LoadWithFade(string sceneName)
        {
            transitioning = true;
            if (!fade.gameObject.activeSelf)
            {
                fade.alpha = 0f;
            }
            fade.gameObject.SetActive(true);
            fade.blocksRaycasts = true;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return FadeTo(1f, SceneFadeOutSeconds);
            yield return new WaitForEndOfFrame();
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName);
            while (operation != null && !operation.isDone) yield return null;
            yield return FadeTo(0f, SceneFadeInSeconds);
            fade.gameObject.SetActive(false);
            transitioning = false;
        }

        private IEnumerator FadeTo(float target, float duration)
        {
            float start = fade.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fade.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }
            fade.alpha = target;
        }

        private void RefreshObjective()
        {
            if (run == null || objectiveText == null) return;
            int seconds = Mathf.CeilToInt(run.TimeRemaining);
            objectiveText.text = "LEVEL 01 · " + MissionName
                + "\n撤离倒计时  " + (seconds / 60).ToString("00") + ":"
                + (seconds % 60).ToString("00")
                + "\nCELL 仓储  $"
                + (extractionZone != null ? extractionZone.CurrentStoredValue : 0)
                + " / $" + run.RequiredValue;
        }

        private void EnsureUi()
        {
            if (canvas != null) return;
            GameObject canvasObject = new GameObject(
                "Mission UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            objectiveText = CreateText("Objective", canvas.transform, 28, TextAnchor.UpperLeft);
            SetRect(objectiveText.rectTransform, new Vector2(30f, -30f),
                new Vector2(600f, 160f), new Vector2(0f, 1f));
            promptText = CreateText("Prompt", canvas.transform, 25, TextAnchor.LowerCenter);
            SetRect(promptText.rectTransform, new Vector2(0f, 35f),
                new Vector2(1100f, 70f), new Vector2(0.5f, 0f));

            resultPanel = new GameObject("Mission Result", typeof(Image));
            resultPanel.transform.SetParent(canvas.transform, false);
            resultPanel.GetComponent<Image>().color = new Color(0.015f, 0.025f, 0.035f, 0.96f);
            Stretch(resultPanel.GetComponent<RectTransform>());
            resultText = CreateText("Result Text", resultPanel.transform, 42, TextAnchor.MiddleCenter);
            Stretch(resultText.rectTransform);
            resultText.rectTransform.offsetMin = new Vector2(200f, 100f);
            resultText.rectTransform.offsetMax = new Vector2(-200f, -100f);
            resultPanel.SetActive(false);

            GameObject fadeObject = new GameObject("Scene Fade", typeof(Image), typeof(CanvasGroup));
            fadeObject.transform.SetParent(canvas.transform, false);
            fadeObject.GetComponent<Image>().color = Color.black;
            Stretch(fadeObject.GetComponent<RectTransform>());
            fade = fadeObject.GetComponent<CanvasGroup>();
            fade.alpha = 0f;
            fade.blocksRaycasts = true;
            fadeObject.SetActive(false);
        }

        private static Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(Text), typeof(Outline));
            textObject.transform.SetParent(parent, false);
            Text text = textObject.GetComponent<Text>();
            text.font = GetUiFont();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.82f, 0.96f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            Outline outline = textObject.GetComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private static Font GetUiFont()
        {
            if (missionFont != null) return missionFont;
            missionFont = GameAssetCatalog.Current != null
                ? GameAssetCatalog.Current.Missions.UiFont
                : null;
            return missionFont;
        }

        private static void SetRect(
            RectTransform rect, Vector2 position, Vector2 size, Vector2 pivot)
        {
            rect.anchorMin = pivot;
            rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private int OreUnitValue => definition != null ? definition.OreUnitValue : 1;
        private string MissionName => definition != null
            ? definition.DisplayName
            : string.Empty;
        private string CaveSceneName => definition != null
            ? definition.CaveSceneName
            : string.Empty;
        private string HomeSceneName => definition != null
            ? definition.HomeSceneName
            : string.Empty;
    }
}
