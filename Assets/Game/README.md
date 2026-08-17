# Assets/Game

`Assets/Game` 是 Supernova 的第一方游戏模块根目录。产品入口、当前功能和运行命令见仓库根目录的 [`README.md`](../../README.md)，专题文档导航见 [`Docs/README.md`](Docs/README.md)。

## 目录职责

| 目录 | 职责 |
| --- | --- |
| `Runtime/` | 按 Audio、Creatures、Gameplay、Input、Missions、Shop、Structures、UI、Voxels、WorldGeneration 等功能组织的运行时代码 |
| `Editor/` | 场景构建、全局资产路径表、动画/玩家/UI/结构/体素创作工具 |
| `Tests/Editor/` | Unity Test Framework + NUnit EditMode 测试 |
| `Config/` | 关卡、世界、矿物、生物、工具、装备、UI、音频和全局资产目录等 ScriptableObject |
| `Prefabs/`、`Animations/`、`Materials/`、`Textures/` | 第一方内容资源 |
| `Structures/` | 固定体素结构与 Jigsaw 模板数据 |
| `Scenes/` | Gallery、模型预览和创作场景；产品/教程场景主要位于 `Assets/Scenes/` |
| `Docs/` | 当前实现、配置指南、架构决策和调研资料 |
| `Research/` | 体素完整性、支撑和物理破坏的研究与原型报告 |

## 主要命名空间

- `Supernova.Audio`
- `Supernova.MinecraftCaves.Creatures`
- `Supernova.Gameplay`
- `Supernova.Infrastructure`
- `Supernova.Inputs`
- `Supernova.MinecraftCaves`
- `Supernova.Missions`
- `Supernova.PortalExample`
- `Supernova.Shop`
- `Supernova.UI`
- `Supernova.Voxels`、`Supernova.Voxels.Integrity`、`Supernova.Voxels.Support`
- `Supernova.WorldGeneration`

目录边界尚未通过第一方 `.asmdef` 固化；运行时代码不得引用 `UnityEditor`，编辑器 API 只能放在 `Editor/` 下。

## 场景与配置入口

- `Assets/Scenes/Home.scene`：构建入口、整合式主菜单、基地、商店、任务舱。
- `Assets/Scenes/DenseJigsawRegion.scene`：正式任务场景；三个 `LevelConfiguration` 当前共用该场景并提供不同种子、目标金额和进度。
- `Assets/Scenes/SpawnShelterStoneTest.scene`：教程和隔离验证。
- `Assets/Scenes/InfiniteCaves.scene`：基础洞穴参考场景，当前未启用构建。

运行时全局引用由 `Config/GameAssetCatalog.asset` 提供；关卡从 `Config/Levels/` 组合 `Config/Worlds/`、生物和宝藏配置。编辑器固定路径统一维护在 `Editor/ProjectAssetPaths.cs`。若资产移动或新增，应先更新这些集中入口以及相应的资产校验测试。

## 资源约定

- 第一方 C#、配置、Prefab、材质和文档留在 `Assets/Game`；第三方内容留在 `Assets/3rd`。
- 资源移动或重命名必须保留 `.meta`，优先在编辑器内完成。
- 不在运行时代码中新增 AssetDatabase 路径；编辑器路径不得散落硬编码。
- 研究/规划文档中的能力不自动视为已实现，判断当前状态应回到场景、配置、代码和测试。
