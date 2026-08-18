using UnityEngine;

namespace Supernova.MinecraftCaves
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class WorldGenerationPassDebugController : MonoBehaviour
    {
        private const int SelectablePassCount = 4;
        private const float PanelWidth = 430f;
        private const float PanelHeight = 360f;

        [SerializeField] private MinecraftCaveInfiniteWorld[] passWorlds =
            new MinecraftCaveInfiniteWorld[SelectablePassCount];
        [SerializeField]
        private MinecraftWorldGenerationDebugPass initialPass =
            MinecraftWorldGenerationDebugPass.NaturalTerrain;
        [SerializeField] private int initialSeed = 18731;
        [SerializeField] private bool showOverlay = true;

        private string seedText;
        private bool presentationDirty = true;

        public MinecraftWorldGenerationDebugPass CurrentPass => initialPass;
        public MinecraftCaveInfiniteWorld CurrentWorld =>
            GetPassWorld(initialPass);
        public int PassWorldCount => passWorlds != null ? passWorlds.Length : 0;

        private void OnEnable()
        {
            if (!initialPass.IsSelectableDebugPass())
            {
                initialPass = MinecraftWorldGenerationDebugPass.NaturalTerrain;
            }

            ConfigurePassWorldsBeforeGeneration();
            seedText = initialSeed.ToString();
            presentationDirty = true;
            ApplyPresentation();
        }

        private void LateUpdate()
        {
            if (presentationDirty || !AreAllPassesReady())
            {
                ApplyPresentation();
                presentationDirty = !AreAllPassesReady();
            }
        }

        private void OnGUI()
        {
            HandleShortcut(Event.current);
            if (!showOverlay)
            {
                return;
            }

            GUILayout.BeginArea(
                new Rect(16f, 16f, PanelWidth, PanelHeight),
                GUI.skin.box);
            GUILayout.Label("DenseJigsaw 世界生成 Pass 调试（原点 4×4）");
            GUILayout.Label("四个阶段已分别缓存；F1–F4 切换不会重新生成。");
            DrawPassButton(
                MinecraftWorldGenerationDebugPass.NaturalTerrain,
                "F1  自然地形");
            DrawPassButton(
                MinecraftWorldGenerationDebugPass.OreGeneration,
                "F2  + 矿物生成");
            DrawPassButton(
                MinecraftWorldGenerationDebugPass.JigsawStructures,
                "F3  + Jigsaw 结构生成");
            DrawPassButton(
                MinecraftWorldGenerationDebugPass.MarkerObjects,
                "F4  + Marker 物件生成");
            DrawSeedControls();

            MinecraftCaveInfiniteWorld currentWorld = CurrentWorld;
            if (currentWorld != null)
            {
                GUILayout.Space(4f);
                GUILayout.Label(
                    $"当前：{currentWorld.GenerationStage}  "
                    + $"{Mathf.RoundToInt(currentWorld.InitialLoadProgress * 100f)}%  "
                    + $"列：{currentWorld.GeneratedChunkCount}/"
                    + $"{currentWorld.RequiredChunkCount}");
                GUILayout.Label(
                    $"缓存完成：{CountReadyPasses()}/{SelectablePassCount}  "
                    + $"世界种子：{currentWorld.WorldSeed}");
            }
            else
            {
                GUILayout.Label("当前阶段未配置 MinecraftCaveInfiniteWorld。");
            }
            GUILayout.Label("F5  隐藏/显示全部调试 UI（截图模式）");
            GUILayout.EndArea();
        }

        public bool SelectPass(MinecraftWorldGenerationDebugPass pass)
        {
            MinecraftCaveInfiniteWorld selectedWorld = GetPassWorld(pass);
            if (selectedWorld == null)
            {
                return false;
            }

            initialPass = pass;
            presentationDirty = true;
            ApplyPresentation();
            return true;
        }

        public bool SetSeedForAllPasses(int seed)
        {
            bool foundWorld = false;
            initialSeed = seed;
            seedText = seed.ToString();
            if (passWorlds == null)
            {
                return false;
            }

            for (int i = 0; i < passWorlds.Length; i++)
            {
                MinecraftCaveInfiniteWorld passWorld = passWorlds[i];
                if (passWorld == null)
                {
                    continue;
                }

                foundWorld = true;
                passWorld.SetGenerationSeedOverride(seed, true);
            }
            presentationDirty = true;
            return foundWorld;
        }

        private void ConfigurePassWorldsBeforeGeneration()
        {
            if (passWorlds == null)
            {
                return;
            }

            int count = Mathf.Min(passWorlds.Length, SelectablePassCount);
            for (int i = 0; i < count; i++)
            {
                MinecraftCaveInfiniteWorld passWorld = passWorlds[i];
                if (passWorld == null)
                {
                    continue;
                }

                MinecraftWorldGenerationDebugPass pass =
                    (MinecraftWorldGenerationDebugPass)(i + 1);
                passWorld.SetGenerationDebugPass(pass, false);
                passWorld.SetGenerationSeedOverride(initialSeed, false);
            }
        }

        private void DrawSeedControls()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("种子", GUILayout.Width(34f));
            seedText = GUILayout.TextField(seedText ?? string.Empty);
            bool hasValidSeed = int.TryParse(seedText, out int parsedSeed);
            bool wasEnabled = GUI.enabled;
            GUI.enabled = hasValidSeed && PassWorldCount > 0;
            if (GUILayout.Button("应用", GUILayout.Width(54f)))
            {
                SetSeedForAllPasses(parsedSeed);
            }
            GUI.enabled = PassWorldCount > 0;
            if (GUILayout.Button("随机 Roll", GUILayout.Width(76f)))
            {
                SetSeedForAllPasses(
                    MinecraftCaveInfiniteWorld.CreateRandomWorldSeed());
            }
            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }

        private void DrawPassButton(
            MinecraftWorldGenerationDebugPass pass,
            string label)
        {
            bool wasEnabled = GUI.enabled;
            GUI.enabled = GetPassWorld(pass) != null && CurrentPass != pass;
            if (GUILayout.Button(label))
            {
                SelectPass(pass);
            }
            GUI.enabled = wasEnabled;
        }

        private void HandleShortcut(Event currentEvent)
        {
            if (currentEvent == null
                || currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.F5)
            {
                showOverlay = !showOverlay;
                presentationDirty = true;
                ApplyPresentation();
                currentEvent.Use();
                return;
            }

            MinecraftWorldGenerationDebugPass pass;
            switch (currentEvent.keyCode)
            {
                case KeyCode.F1:
                    pass = MinecraftWorldGenerationDebugPass.NaturalTerrain;
                    break;
                case KeyCode.F2:
                    pass = MinecraftWorldGenerationDebugPass.OreGeneration;
                    break;
                case KeyCode.F3:
                    pass = MinecraftWorldGenerationDebugPass.JigsawStructures;
                    break;
                case KeyCode.F4:
                    pass = MinecraftWorldGenerationDebugPass.MarkerObjects;
                    break;
                default:
                    return;
            }

            SelectPass(pass);
            currentEvent.Use();
        }

        private MinecraftCaveInfiniteWorld GetPassWorld(
            MinecraftWorldGenerationDebugPass pass)
        {
            if (!pass.IsSelectableDebugPass() || passWorlds == null)
            {
                return null;
            }

            int index = (int)pass - 1;
            return index >= 0 && index < passWorlds.Length
                ? passWorlds[index]
                : null;
        }

        private void ApplyPresentation()
        {
            if (passWorlds == null)
            {
                return;
            }

            for (int i = 0; i < passWorlds.Length; i++)
            {
                MinecraftCaveInfiniteWorld passWorld = passWorlds[i];
                if (passWorld == null)
                {
                    continue;
                }

                MinecraftWorldGenerationDebugPass pass =
                    (MinecraftWorldGenerationDebugPass)(i + 1);
                passWorld.SetDebugPresentationVisible(
                    pass == initialPass,
                    showOverlay);
            }
        }

        private bool AreAllPassesReady()
        {
            return CountReadyPasses() == SelectablePassCount;
        }

        private int CountReadyPasses()
        {
            if (passWorlds == null)
            {
                return 0;
            }

            int readyCount = 0;
            int count = Mathf.Min(passWorlds.Length, SelectablePassCount);
            for (int i = 0; i < count; i++)
            {
                if (passWorlds[i] != null && passWorlds[i].IsInitialLoadComplete)
                {
                    readyCount++;
                }
            }
            return readyCount;
        }
    }
}
