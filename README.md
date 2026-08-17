# Supernova

Supernova 是一个基于 Unity/Tuanjie 2022.3 的体素洞穴游戏原型。当前主线已经从单一洞穴演示扩展为完整的任务闭环：玩家从 Home 基地进入任务、探索程序化洞穴与高密度 Jigsaw 结构、采集矿物和宝藏、对抗生物，并在倒计时结束后结算收益、推进关卡和购买装备。

## 当前入口

项目使用 Tuanjie `2022.3.62t11`（Tuanjie `1.9.3`）。`ProjectSettings/EditorBuildSettings.asset` 当前启用的场景为：

| 顺序 | 场景 | 用途 |
| ---: | --- | --- |
| 0 | `Assets/Scenes/Home.scene` | 产品入口、整合式主菜单、基地、商店和任务舱 |
| 1 | `Assets/Scenes/DenseJigsawRegion.scene` | 三个正式关卡共用的洞穴任务场景 |
| 2 | `Assets/Scenes/SpawnShelterStoneTest.scene` | 新手教程与隔离玩法验证 |

`Assets/Scenes/InfiniteCaves.scene` 保留为基础无限洞穴参考场景，目前未加入构建。`CombatTest`、`JigsawSuperflat`、`WorldGenerationPreview`、`VoxelStructureEditor` 及 `Experiments/`、`Prototypes/` 下的场景用于开发、预览或专项验证。

## 已实现系统

- **任务与经济**：Home 基地、任务舱、三个顺序关卡、限时采集、自动撤离结算、持久化进度、货币和商店。
- **体素世界**：按 X/Z 流送的 `32 × 256 × 32` 体素柱、确定性三维洞穴密度、Marching Cubes 网格、碰撞体、运行时编辑和跨柱重建。
- **Dense Jigsaw 世界**：正式任务在基础洞穴配置上叠加高密度 Jigsaw 结构、外置降落舱、检查点传送门和有限/无限区域控制。
- **结构生成**：固定体素结构、Random Spread、Concentric Rings、Structure Set 竞争、显式 Socket、模板 Piece、Processor、缓存和柱级裁剪。
- **采集与破坏**：五类矿物配置、整片矿脉回收、价值/质量计算、宝藏、炸弹破坏，以及失去支撑后生成动态体素刚体的完整性链路。
- **玩家与装备**：第一/第三人称相机、移动/跳跃/蹲伏、近战、投掷与召回探险镐、手电筒、地形发生器、炸弹、传送门发生器和喷气背包。
- **生物**：配置化生成、状态机、近战与受击、体素 A* 寻路、跳跃/下落和卡住恢复。
- **表现与界面**：UGUI + TextMeshPro 主菜单、HUD、暂停、加载、任务、装备和输入重绑；洞穴植被、柔化点光衰减、晶体矿石材质、音频事件与空间音效。

## 运行流程

```text
Home.scene
  ├─ MainMenuController        主菜单与镜头过渡
  ├─ HomeShopController        装备商店
  └─ MissionGameLoop           选择当前 LevelConfiguration
          ↓
DenseJigsawRegion.scene
  ├─ MinecraftCaveInfiniteWorld
  │    ├─ DenseJigsawWorldConfiguration
  │    ├─ MinecraftWorldGenerationConfiguration
  │    ├─ InfiniteVoxelWorld / MarchingCubesMesher
  │    └─ 矿物、结构、生物、宝藏与体素完整性
  └─ MissionGameLoop           采集、倒计时与结算
          ↓
Home.scene                     结算、关卡推进与购买
```

`Assets/Game/Config/GameAssetCatalog.asset` 是运行时全局引用入口，集中保存关卡列表、场景名、输入、UI、音频和效果资产。关卡再通过 `LevelConfiguration` 组合世界、怪物与宝藏配置。编辑器工具的固定资产路径统一维护在 `Assets/Game/Editor/ProjectAssetPaths.cs`；新增或移动资产时应更新目录或全局路径表，不应在业务代码中散落硬编码路径。

## 项目结构

```text
Assets/
├─ 3rd/                         第三方模型、动画、插件与素材
├─ Game/                        第一方代码、配置、资源、测试与文档
│  ├─ Runtime/
│  │  ├─ Audio/                 音频管理、Cue 与事件
│  │  ├─ Creatures/             生物行为、生成与体素寻路
│  │  ├─ Gameplay/              玩家、武器、装备、投射物与交互
│  │  ├─ Infrastructure/        GameAssetCatalog 等全局资产入口
│  │  ├─ Input/                 Input System、重绑与按键提示
│  │  ├─ MinecraftCaves/        洞穴生态、表面植被与出生结构
│  │  ├─ Missions/              关卡配置、任务运行与撤离
│  │  ├─ PortalExample/         传送门渲染、穿越与 Dense 集成
│  │  ├─ Shop/                  经济与基地商店
│  │  ├─ Structures/            Jigsaw 定义、选址、布局与落地
│  │  ├─ UI/                    主菜单、HUD、暂停、装备与世界 UI
│  │  ├─ Voxels/                体素数据、网格、采矿、完整性与支撑
│  │  └─ WorldGeneration/       Dense Jigsaw 世界覆盖与特征混合
│  ├─ Editor/                   场景构建、资产生成和创作工具
│  ├─ Tests/Editor/             NUnit / Unity Test Framework EditMode 测试
│  ├─ Config/                   ScriptableObject 配置资产
│  ├─ Prefabs/                  第一方游戏 Prefab
│  ├─ Structures/               体素结构数据
│  ├─ Docs/                     实现、配置、决策与调研文档
│  └─ Research/                 体素物理与支撑系统研究/实验报告
├─ Scenes/                      产品、教程、预览、实验与原型场景
├─ UI/                          UGUI/TMP Prefab、遗留 UI Toolkit 与贴图
├─ Materials/、Prefabs/         跨模块共享资源
└─ Settings/                    URP 与渲染配置
```

第一方代码目前仍进入默认 `Assembly-CSharp` / `Assembly-CSharp-Editor`，仓库中没有第一方 `.asmdef`。目录和命名空间表达模块边界，但尚未形成编译边界。

## 开发与验证

使用 Tuanjie Hub 打开仓库根目录，日常运行从 `Assets/Scenes/Home.scene` 开始。批处理命令中的 `<Editor>` 应替换为本机 Tuanjie Editor 可执行文件：

```powershell
& '<Editor>' -batchmode -quit -projectPath . -runTests -testPlatform EditMode -testResults Logs/EditMode.xml
& '<Editor>' -batchmode -quit -projectPath . -buildWindows64Player Builds/Windows/Supernova.exe
```

提交前应核对：

1. `Home`、`DenseJigsawRegion` 和 `SpawnShelterStoneTest` 的构建开关与顺序符合目标版本。
2. Unity/Tuanjie 完成脚本刷新且 Console 没有新增第一方编译错误。
3. EditMode 测试结果来自本次工作区运行，而不是文档中的历史数字。
4. 场景、Prefab、材质或 UI 改动附带截图或录屏；资源移动保留 `.meta`。

不要提交 `Library/`、`Temp/`、`Logs/`、`UserSettings/` 或本地构建输出。

## 文档

文档导航见 [`Assets/Game/Docs/README.md`](Assets/Game/Docs/README.md)。常用入口：

- [地形生成运行时链路](Assets/Game/Docs/MinecraftCaves世界生成与Voxel依赖.md)
- [Jigsaw 结构配置手册](Assets/Game/Docs/Jigsaw结构配置手册.md)
- [Jigsaw 结构生成算法](Assets/Game/Docs/Jigsaw结构生成.md)
- [矿物生成与体素链路](Assets/Game/Docs/Minecraft矿物生成与项目体素链路.md)
- [固定体素结构](Assets/Game/Docs/FixedVoxelStructures.md)
- [生物状态与体素寻路](Assets/Game/Docs/生物和行为树.md)
- [UI 技术决策](Assets/Game/Docs/UI/ADR-001-Runtime-UI-Stack.md)
- [UI 审计与重构 PRD](Assets/Game/Docs/UI/UI_AUDIT_AND_REFACTOR_PRD.md)
- [游戏内显示文本清单](LANGUAGES.md)

文档中的“调研”“目标”“PRD”“实验报告”用于记录方案与历史，不等同于已实现功能；当前事实以场景、配置资产、运行时代码和本次测试结果为准。
