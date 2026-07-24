# Game

`Assets/Game` 是 Supernova 的第一方游戏模块根目录。项目总览、运行链路、验证状态和已知架构问题见仓库根目录的 `README.md`。

## 目录职责

- `Runtime/`：游戏运行时代码。
- `Editor/`：场景构建、动画、玩家、生物和体素编辑工具。
- `Tests/Editor/`：EditMode 测试。
- `Animations/`、`Prefabs/`、`Config/`、`Structures/`：游戏内容与配置。
- `CreatureAssets/`：示例生物和烘焙数据。
- `Scenes/`：Gallery、模型预览、无限洞穴示例和体素结构编辑场景。
- `Docs/`：各子系统的设计与实现文档。

## 代码命名空间

- `Supernova.Voxels`
- `Supernova.Gameplay`
- `Supernova.Effects`
- `Supernova.UI`
- `Supernova.MinecraftCaves`
- `Supernova.MinecraftCaves.Creatures`

目录统一并不代表程序集边界已经完成。当前第一方代码仍缺少 asmdef，这是后续架构整理的优先事项。

## 场景入口

当前产品主场景仍位于 `Assets/Scenes/InfiniteCaves.scene`。`Scenes/` 下其他场景主要用于功能演示、模型预览和编辑工作流，不应与产品构建入口混淆。