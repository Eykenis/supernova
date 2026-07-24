# Supernova

Supernova 是一个基于 Unity/Tuanjie 2022.3 的体素洞穴实验型游戏项目。当前产品主线围绕 `InfiniteCaves` 场景展开，核心目标是把无限三维洞穴、可修改体素、玩家工具、生物导航和战斗系统组合成一个可持续扩展的游戏原型。

本文根据当前代码库以及 `Assets/Game/Docs` 中保留的设计文档整理，重点描述项目现状、代码边界和维护问题。性能优化不属于本文当前优先级。

## 当前状态

- 主场景：`Assets/Scenes/InfiniteCaves.scene`
- 核心世界实现：`Supernova.MinecraftCaves.MinecraftCaveInfiniteWorld`
- 体素基础设施：`Supernova.Voxels`
- 玩家与通用玩法：`Supernova.Gameplay`
- UI：`Supernova.UI`
- 生物系统：`Supernova.MinecraftCaves.Creatures`
- 第三方资源统一位于：`Assets/3rd`
- 第一方 C#、编辑器工具和测试统一位于：`Assets/Game`
- 已删除旧的 `Room_Template`、`Room_Structure`、`ProceduralCaveWorld` 链路。

## 项目结构

```text
Assets/
├─ 3rd/                         第三方模型、动画、插件和素材
├─ Game/                        第一方游戏代码与功能资源
│  ├─ Runtime/
│  │  ├─ Animation/             运行时动画辅助
│  │  ├─ Creatures/             生物行为、物理移动和体素寻路
│  │  ├─ Effects/               区域效果、磁力表现
│  │  ├─ Gameplay/              玩家、战斗、工具、炸弹、相机
│  │  ├─ Physics/               矿车和可吸附物体物理组件
│  │  ├─ UI/                    HUD 与展示逻辑
│  │  └─ Voxels/                体素数据、网格、采矿和结构编辑
│  ├─ Editor/
│  │  ├─ Animation/             动画生成、重定向和合成工具
│  │  ├─ Creatures/             生物形状烘焙与验证工具
│  │  ├─ Player/                玩家模型与 Animator 配置工具
│  │  └─ Voxels/                固定体素结构编辑工具
│  ├─ Tests/Editor/             EditMode 测试
│  ├─ Animations/               第一方 Animator 与动画片段
│  ├─ Config/                   体素类型等配置资产
│  ├─ CreatureAssets/           生物示例与烘焙数据
│  ├─ Prefabs/                  Player 等游戏预制体
│  ├─ Scenes/                   Gallery、模型和结构编辑场景
│  ├─ Structures/               固定体素结构资产
│  └─ Docs/                     专题设计与实现文档
├─ Scenes/                      产品场景与 Unity 示例场景
├─ Prefabs/                     跨模块共享预制体
├─ Materials/                   跨模块共享材质
└─ Settings/                    URP 与渲染配置
```

## 核心运行链路

```text
Assets/Scenes/InfiniteCaves.scene
  ├─ MinecraftCaveInfiniteWorld
  │  ├─ MinecraftCaveDensityField
  │  ├─ MinecraftCaveVolumeGenerator
  │  ├─ InfiniteVoxelWorld
  │  ├─ MarchingCubesMesher
  │  └─ VoxelStructureAsset
  ├─ VoxelPlayerController
  │  ├─ PerspectiveCameraController
  │  ├─ VoxelPlayerInteractor
  │  ├─ PlayerToolController
  │  ├─ FirstPersonCartAttractor
  │  └─ CharacterCombat
  ├─ CreatureBehaviorAgent
  │  ├─ CreatureVoxelNavigation
  │  └─ CreaturePhysicsMotor
  └─ GameHudController
```

### 洞穴与体素

洞穴使用绝对世界坐标采样确定性密度场，组合 Cheese、Spaghetti、Noodle 和 Pillar 等结构。正密度表示固体，负密度表示空气。世界以 `32³` 体素 Chunk 存储，通过 Marching Cubes 生成可渲染和可碰撞网格。

`MinecraftCaveInfiniteWorld` 负责观察者周围 Chunk 的生成、提交、网格构建、卸载和编辑。玩家采矿、放置和炸弹破坏最终都通过体素编辑接口修改世界并触发受影响 Chunk 的重建。

### 玩家与工具

玩家支持第一/第二/第三人称视角、CharacterController 移动、跳跃、蹲伏、近战、挖矿、磁力工具和炸弹。`PlayerToolController` 管理工具栏选择，HUD 展示生命值和当前工具。

### 生物

生物系统使用体素可站立查询和累计代价 A* 导航。`CreatureBehaviorAgent` 当前包含 Idle、Wander、Pursue、Attack、Hurt 和 Dead 状态，并通过 `CreaturePhysicsMotor` 执行路径步骤。

### 固定体素结构

固定结构使用 `VoxelStructureAsset` 持久化密度和类型数据。结构编辑流程由 `Assets/Game/Editor/Voxels` 中的工具以及 `VoxelStructureEditor.scene` 支持。

## 主要文档

- `Assets/Game/Docs/Minecraft洞穴与无限区块生成.md`
- `Assets/Game/Docs/MinecraftCaves世界生成与Voxel依赖.md`
- `Assets/Game/Docs/体素与距离场生成.md`
- `Assets/Game/Docs/FixedVoxelStructures.md`
- `Assets/Game/Docs/生物体素寻路实现.md`
- `Assets/Game/Docs/生物和行为树.md`
- `Assets/Game/Docs/FIRST_PERSON_ANIMATION.md`
- `Assets/Game/Docs/CartPhysicsAndPlayerTools.md`
- `Assets/Game/Docs/BOMB_SYSTEM.md`
- `Assets/Game/Docs/SparseAnimationResearch.md`
- `Assets/Game/Docs/DRG_TECH_GOALS.md`

Blender、NLA 和外部动画迁移教学文档已从项目资源中删除；项目文档只保留与当前 Unity 游戏实现直接相关的内容。

## 当前验证状态

整理完成后的验证结果：

- Unity 脚本刷新和编译：通过，无第一方编译错误。
- `InfiniteCaves` 场景校验：通过，缺失脚本 `0`，损坏 Prefab `0`。
- EditMode 测试：共 `39` 项，`37` 通过，`2` 失败。

仍失败的测试：

1. `BombAndVoxelEffectTests.ViewerMovement_RefreshesStreamingWhileMeshesAreStillQueued`
   - 期望 Viewer Chunk 为 `(1, 0, 0)`，实际仍为 `(0, 0, 0)`。
2. `FirstPersonAnimationControllerTests.UnifiedController_DrivesMuryotaisuAnimatorContract`
   - 反射调用 `SetAnimationState` 时参数数量与当前方法签名不一致。

第二项是测试和实现签名漂移，第一项需要进一步确认测试对流送刷新时机的预期是否仍符合当前世界生命周期。

## 已知架构与维护问题

### P0：入口与验证

1. **构建入口未同步**
   - 当前实际主场景是 `Assets/Scenes/InfiniteCaves.scene`。
   - `ProjectSettings/EditorBuildSettings.asset` 仍只启用 `Assets/Scenes/SampleScene.scene`。

2. **全量测试尚未全绿**
   - 两项失败会降低后续重构的安全性，应优先修复或更新已经过期的断言。

### P1：模块边界

3. **缺少第一方程序集定义**
   - 第一方代码仍主要进入默认 `Assembly-CSharp` 与 `Assembly-CSharp-Editor`。
   - 目录表达了模块，但编译器没有强制依赖方向。
   - 建议拆分 `Supernova.Voxels`、`Supernova.Gameplay`、`Supernova.MinecraftCaves`、`Supernova.UI`、`Supernova.Editor` 和测试程序集。

4. **逻辑依赖仍存在回环**
   - `MinecraftCaves` 合理地依赖 `Voxels`。
   - `VoxelPlayerInteractor` 和 `VoxelDestructionReceiver` 仍知道具体的 `MinecraftCaveInfiniteWorld`。
   - 建议让通用体素层只依赖 `IVoxelTerrain`，把具体适配器放到 MinecraftCaves 集成层。

5. **核心 MonoBehaviour 职责过大**
   - `MinecraftCaveInfiniteWorld` 同时负责生成调度、Chunk 生命周期、网格、编辑、出生点、Viewer 控制和调试显示。
   - `VoxelPlayerController` 同时负责输入、移动、战斗、采矿、磁力、生命值、动画和状态机。
   - `CreatureBehaviorAgent` 同时负责决策、导航、战斗、生命值和调试。
   - `GameHudController` 同时负责运行时创建、对象查找、视图构造、绑定和展示。

6. **场景装配过于隐式**
   - 多处通过 `FindObjectOfType`、`Camera.main`、Tag 和运行时 `AddComponent` 自动补齐引用。
   - 建议由 `InfiniteCaves` 的 Bootstrap/Composition Root 显式连接 World、Player、HUD 和 Creature。

### P2：代码整洁度

7. **命名空间仍不完全统一**
   - 少量脚本仍处于全局命名空间。
   - Editor 工具同时使用 `Supernova.MinecraftCaves.Editor` 和 `Supernova.EditorTools.*`。

8. **多个公共类型集中在单文件**
   - `CharacterCombat.cs`、`AreaEffect.cs`、`GameHudController.cs` 等文件包含多个高关注度公共类型，降低按类型检索的可读性。

9. **测试依赖私有实现**
   - 多个测试通过反射调用私有方法或访问私有字段，重命名和拆分类很容易造成测试失效。
   - 建议把纯逻辑提取为可直接测试的 C# 类，并增加主场景/Prefab 的 PlayMode 装配测试。

10. **示例与产品场景边界不清晰**
    - `Assets/Scenes/InfiniteCaves.scene`、`Assets/Game/Scenes/MinecraftCaveInfiniteWorld.scene` 和 Gallery/Model 场景并存。
    - 建议把非产品入口统一放入 `Samples` 或 `Authoring` 子目录。

## 推荐整理顺序

1. 把 `InfiniteCaves.scene` 设为明确的构建入口。
2. 修复当前两项 EditMode 测试。
3. 添加第一方 asmdef，固化依赖方向。
4. 移除 `Voxels -> MinecraftCaves` 的反向依赖。
5. 逐步拆分 World、Player、Creature 和 HUD 四个超大协调类。
6. 最后统一命名空间、公共类型文件和示例场景位置。

## 维护约定

- 第三方内容只放在 `Assets/3rd`，第一方代码不得再引用 `Assets/3rdChara`。
- 第一方 C#、Editor 工具和测试统一放在 `Assets/Game`。
- 运行时代码不得引用 `UnityEditor`；编辑器工具必须位于 `Editor` 目录。
- 新增硬编码资产路径时集中到 Editor-only 路径目录，并添加存在性检查。
- 删除或移动资源时必须保留 `.meta`，并在完成后执行 Unity 刷新、场景校验和测试。