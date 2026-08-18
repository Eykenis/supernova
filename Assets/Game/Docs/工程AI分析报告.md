# Supernova 工程 AI 分析报告

> 分析日期：2026-08-18  
> 分析对象：当前工作区 `D:/TuanjieProjects/supernova`  
> 引擎：Tuanjie `2022.3.62t11` / Editor `1.9.3`  
> 性质：基于源码、场景、配置资产和 ProjectSettings 的静态工程分析  
> 验证边界：本轮未运行 EditMode/PlayMode 测试，未生成 Player Build

## 1. 执行摘要

Supernova（Player Settings 中产品名为 `Novacraft`）已经不是单一体素技术演示，而是具备完整产品主循环的 PC 游戏原型：玩家从 Home 基地选择任务，经任务舱进入 Dense Jigsaw 洞穴，完成探索、采集、战斗、宝藏搬运和撤离，再回到基地结算、推进关卡并购买装备。

项目最有价值的技术资产是可编辑、可流送、可破坏的确定性体素世界，以及叠加其上的 Jigsaw 结构、洞穴生态、生物寻路和动态体素刚体链路。工程同时具备丰富的 ScriptableObject 配置、场景/资产生成工具和 EditMode 自动化测试，说明项目正在从“玩法验证”进入“系统整合与产品化”阶段。

| 维度 | 评价 | 说明 |
| --- | --- | --- |
| 玩法闭环 | 良好 | Home、任务、采集、战斗、撤离、经济和关卡推进已贯通 |
| 技术独特性 | 很强 | 体素流送、Marching Cubes、Jigsaw、完整性物理形成技术壁垒 |
| 数据驱动 | 良好 | 96 个配置资产覆盖主要内容系统 |
| 自动化测试 | 中上 | 61 个测试文件、约 571 个测试方法，但集中于 EditMode |
| 可维护性 | 中等 | 模块目录清楚，但存在多个巨型协调器且无第一方 `.asmdef` |
| 性能可控性 | 中等 | 已有异步、对象池、ProfilerMarker 和分帧预算，缺正式性能门禁 |
| 发布准备度 | 中低 | CI、PlayMode/构建验证、存档演进与发布配置仍需补齐 |
| AI 协作适配度 | 良好 | 文档、路径表、测试较强；巨型类和场景隐式依赖会放大改动风险 |

最高优先级不是继续横向增加大型系统，而是固化现有闭环：建立可重复验证基线，拆分高耦合协调器，建立程序集边界，收敛存档方案，并为世界生成建立性能预算。

## 2. 分析方法与规模快照

本次检查了 `ProjectSettings`、`Packages`、第一方 Runtime/Editor/Test 代码、Config、正式/实验场景、现有文档、资源体量、`.meta` 配对、Git LFS 和当前工作区状态。统计为当前快照，物理行包含空行和注释。

| 指标 | 当前值 | 口径 |
| --- | ---: | --- |
| 第一方 C# 文件 | 332 | `Assets/Game/**/*.cs` |
| 第一方 C# 物理行 | 约 123,246 | 非有效代码行 |
| 运行时 / 编辑器 / 测试 C# | 231 / 40 / 61 | 目录统计 |
| 测试方法 | 约 571 | `[Test]` 与 `[UnityTest]` 静态计数 |
| 场景 | 13 | `.scene` 与 `.unity` |
| 第一方/UI Prefab | 57 | `Assets/Game` 与 `Assets/UI` |
| 配置资产 | 96 | `Assets/Game/Config/**/*.asset` |
| 第一方 `.asmdef` | 0 | `Assets/Game` 范围 |
| `Assets/3rd` | 约 11,370.6 MiB / 8,967 文件 | 当前磁盘体量 |
| `Assets/Game` | 约 827.4 MiB / 2,117 文件 | 当前磁盘体量 |

结论置信度分三类：直接来自代码/YAML/ProjectSettings 的事实为高；跨多个信号的静态推断为中；必须经 Editor、Profiler、Player Build 或玩法检查确认的内容为待验证。

## 3. 产品闭环与场景

```text
Home.scene
  ├─ 主菜单、设置、新游戏引导
  ├─ 基地、商店、装备和经济
  └─ MissionGameLoop 选择 LevelConfiguration
          ↓
DenseJigsawRegion.scene
  ├─ 流送体素洞穴与 Jigsaw 结构
  ├─ 矿物、生物、宝藏和表面生态
  ├─ 采集、战斗、搬运和破坏
  └─ 倒计时结束或满足条件后提前撤离
          ↓
Home.scene：结算、经济更新和关卡推进
```

`EditorBuildSettings` 当前启用顺序为 Home、DenseJigsawRegion、SpawnShelterStoneTest；InfiniteCaves 禁用并作为参考。CombatTest、JigsawSuperflat、WorldGenerationPreview、WorldGenerationPassDebug、VoxelStructureEditor 及 Experiments/Prototypes 场景用于开发验证。

三个正式关卡共用 Dense 场景和同一套世界/生物/宝藏配置，差异如下：

| 关卡 | Seed | 时限 | 资金目标 |
| ---: | ---: | ---: | ---: |
| 1 | 11451 | 180 秒 | 2000 |
| 2 | 11523 | 240 秒 | 5000 |
| 3 | 114599 | 300 秒 | 3000 |

三个正式关卡的 `displayName` 均为空。第三关目标低于第二关可能是有意节奏，也可能是未收敛平衡数据，发布前应由策划确认并记录意图。

## 4. 总体架构

```text
场景与表现：Home / Dense / Tutorial / HUD / MainMenu
             ↓
玩法编排：MissionGameLoop / PlayerToolController / CreatureBehaviorAgent
             ↓
领域配置：Level / World / Feature / Tool / Spawn ScriptableObjects
             ↓
体素与生成：MinecraftCaveInfiniteWorld / InfiniteVoxelWorld / Jigsaw / Mesher
             ↓
基础设施：GameAssetCatalog / Input / Audio / URP / PlayerPrefs
```

目录和命名空间已表达 `Voxels`、`Gameplay`、`UI`、`MinecraftCaves`、`Creatures`、`Structures`、`Missions`、`Shop`、`Input`、`Audio`、`Effects`、`Infrastructure` 和 `WorldGeneration` 等领域，这是良好的逻辑边界。

但边界没有由第一方 `.asmdef` 固化，运行时主要进入 `Assembly-CSharp`，编辑器和测试进入 `Assembly-CSharp-Editor`。这会扩大重编译范围，使跨模块依赖容易增长，也缺少清晰的内部 API 可见性策略。

工程有两套互补的集中入口：运行时 `GameAssetCatalog.asset` 保存关卡、UI、输入、音频、特效和场景查找引用；编辑器 `ProjectAssetPaths.cs` 集中管理目录、固定资产、场景和查找名称。`UiHierarchyPaths` 与 Shader 名称常量进一步减少字符串散落，符合仓库禁止硬编码路径的规则。

`GameAssetCatalog.Current` 依赖 ScriptableObject 的 `OnEnable/OnDisable` 建立静态实例，轻量但受加载时序影响。应持续覆盖无 Catalog、重复 Catalog、Domain Reload 配置变化和首个消费者提前访问等情况。

## 5. 核心技术系统

### 5.1 体素数据与流送

`VoxelColumnChunkData` 使用 `32 × 256 × 32` 的完整 X/Z 柱，每柱 262,144 个采样点；密度为 `float[]`，类型为 `VoxelTypeId[]`。Y 不参与柱坐标，负密度为空气，非负密度为实体。`InfiniteVoxelWorld` 用 `Dictionary<Vector2Int, InfiniteVoxelChunk>` 管理已加载柱。

若密度和类型各按 4 字节粗估，单柱基础数组约 2 MiB；默认需求集合常量为 49 柱时，仅基础体素数据接近 98 MiB，尚未计入网格、碰撞体、缓存、表面生态和临时数组。实际值必须用 Memory Profiler 验证，但柱生命周期与数组复用应视为首要指标。

`MinecraftCaveInfiniteWorld` 约 7,465 行，是工程核心协调器，承担配置解析、Viewer 流送、异步体素生成、Marching Cubes 调度、主线程 Mesh/Collider 提交、Dense Jigsaw 选择、矿物/结构/生物/宝藏/出生点生成、运行时体素编辑、对象池和加载进度发布。

其异步模型使用 `Task.Run`、取消句柄、任务版本号和主线程提交队列；生成与网格分别限制并发，销毁有数量/毫秒预算，并已有多组 `ProfilerMarker`。方向正确，但职责过度集中，局部修改容易影响整个流送状态机。建议先用现有测试锁定行为，再提取纯数据生成器、调度器、提交器和实体生成器。

### 5.2 Marching Cubes 与运行时编辑

`MarchingCubesMesher` 约 1,596 行，负责跨柱采样和按体素类型构建分段网格。世界以 32 体素高的 Mesh Section 处理柱网格；Dense 当前 `worldSectionCount = 2`，配置展示高度为 64，而底层柱仍保留 256 高容量。

运行时编辑把世界坐标写回柱数据，并对本柱、边界相邻柱和版本化 Mesh 任务排队。采矿、炸弹、实体投射物和完整性系统复用这一入口，保持了“体素数据为真值、Mesh/Collider 为派生结果”的正确方向。

### 5.3 Dense Jigsaw

Jigsaw 已实现配置化 Feature/Piece/Connector/Processor、区域选址、结构族竞争、Socket/朝向/Role/Joint 匹配、多次布局尝试、Template 与程序化 Piece、布局缓存、空间索引、柱级裁剪、Spawn/Checkpoint Marker，以及 Fill Downwards、Clear Upwards、Weathering 等 Processor。

当前 Dense 配置为无限世界、2 个 Section、结构密度 2、基础网格间距 6、最多 256 Piece、深度 48、半径 48、16 次布局尝试、每连接器 32 次放置尝试。`preventStructureIntersections` 关闭，允许布局交织，能提高密度和意外性，也增加不可达空间和碰撞冲突风险。

`JigsawStructureGenerator` 约 3,731 行，同时有约 3,583 行专项测试，是“复杂度高但测试投入也高”的模块。应继续保护确定性、缓存、不变量与上限，不宜在无固定 Seed 回归时大改随机调用顺序。

### 5.4 世界生成 Pass 与生态

世界流水线由基础洞穴密度与边界、固定/Jigsaw 结构、矿物 Feature、网格碰撞、洞穴表面层与 Grass/Vine Brush、出生结构/生物/宝藏提交组成。新增的 `MinecraftWorldGenerationDebugPass`、Utility、Controller 和独立调试场景支持逐 Pass 观察，是降低 PCG 调参与 AI 修改盲区的重要可解释性工具。

### 5.5 体素完整性

完整性链路包含连通/支撑搜索、无限世界适配、动态体素构建、质量属性、BVH 射线查询、凸分解、Collider、`DynamicVoxelBody` 生命周期以及采矿/爆炸联动。这是玩法差异化资产。主要待测风险是瞬时分配、Collider 烹饪、碎块上限、极端连通区域搜索和回收策略，需要压力场景而不只是纯逻辑测试。

### 5.6 生物、玩家与装备

生物系统包含配置化生成、状态机、体素 A*、物理 Motor、跳跃/下落、近战/受击和卡住恢复。寻路抽象出 `IVoxelSolidityQuery`、节点生成、最小堆和路径对象，可测试性良好；`CreatureBehaviorAgent` 约 1,362 行，后续可拆分感知、决策和 Motor 命令，并建立多生物路径请求预算。

玩家系统覆盖第一/第三人称、移动/跳跃/蹲伏/坠落、近战、投掷召回镐、磁力、手电、炸弹、地形发生器、传送门和喷气背包。`VoxelPlayerController` 约 3,161 行，`PlayerToolController` 约 1,170 行。建议把输入采样、运动、相机姿态、落地反馈和环境查询拆为独立组件，保留薄协调层。

### 5.7 UI、输入与音频

当前产品 UI 栈为 UGUI + TextMeshPro，覆盖主菜单、HUD、暂停、任务、装备、商店、世界标签、怪物血条、出生点、加载和新手引导。`GameHudController` 约 4,719 行，是第二大运行时文件，同时承担多个页面、设置、流程与动态查找；应优先拆出设置、暂停、任务、Hotbar 和世界 UI 协调器。

输入基于 Input System `1.14.4-t1`，支持重绑定、提示解析和 TMP Glyph。Project Settings 的 `activeInputHandler = 2` 表示同时启用旧/新输入后端，应确认是兼容需求还是可收敛。音频通过 Manager、Cue、Event 和 Request 解耦，结构合理。

## 6. 内容生产与资产

96 个配置资产已覆盖关卡、世界、5 类矿物、8 个 Jigsaw/检查点结构族、3 类怪物、宝藏、工具、装备、商店、UI 和 22 个音频 Cue。优势是策划参数与代码解耦，且已有 `OnValidate`、`IsComplete` 与资产测试；薄弱点是跨资产 GUID 和加载时序仍需 Editor 验证。

40 个编辑器脚本覆盖场景构建、Catalog/Input、商店/武器/宝藏/生物生成、UI/Glyph/肖像、玩家模型/动画/Shader、体素结构创作以及世界生成预览。这些 Builder 使复杂资产可重建，但需要保证幂等性、集中路径和清晰菜单说明，避免重复执行造成序列化漂移。

`Assets/3rd` 约 11.1 GiB，Git LFS 已覆盖该目录。最大单文件是约 311 MiB 的 `.unitypackage`，另有大量 64–136 MiB 纹理源文件。它会显著增加克隆、导入、CI 缓存、构建审计和许可证管理成本。应先生成“正式场景/Prefab 实际依赖 → 第三方资源”清单，再迁移未引用源包和备份；不可在没有依赖报告时直接删除。

## 7. 质量与验证

约 571 个测试方法覆盖体素、Marching Cubes、矿物、Jigsaw、检查点、完整性、支撑、寻路、战斗、任务、经济、装备、武器、UI、输入、音频、Shader 和场景资产不变量。静态扫描未发现 `[Ignore]` / `[Explicit]`，也未发现常见 `TODO/FIXME/HACK/NotImplementedException` 标记；这不等于没有未完成设计。

主要缺口：

- 测试全部位于 `Tests/Editor`，未发现独立 PlayMode 测试程序集。
- 未发现仓库内 CI 配置。
- `Logs/` 未发现可用于本次结论的 Test Runner XML。
- 本轮未运行测试：Tuanjie Editor 正打开当前工程和 WorldGenerationPassDebug 场景，无法可靠确认是否处于 Play Mode；遵循规则，未擅自退出或并发启动 EditMode 测试。
- 本轮未生成 Windows Player Build，Shader Variant、裁剪、首帧和 Player-only 行为仍待验证。

因此“存在 571 个测试方法”不等于“本次全部通过”。建议验证金字塔为：纯逻辑 EditMode → 资产/场景 EditMode → Home 到任务结算的 PlayMode 冒烟 → 固定 Seed 回归 → 性能压力场景 → Windows Player Build 冒烟。

## 8. 性能与稳定性

已有正向措施包括：后台生成和网格任务、分别限制并发、版本化丢弃过期结果、取消离开需求集合的任务、主线程提交/销毁预算、Chunk/Mesh/宝藏/怪物池、Jigsaw 缓存与空间索引、细粒度 ProfilerMarker、表面生态快照与 LOD。

| 风险 | 依据 | 应采集指标 |
| --- | --- | --- |
| 柱内存高峰 | 单柱 262,144 采样、默认 49 柱 | 已加载柱、体素数组 MiB、峰值内存 |
| Mesh/Collider 尖峰 | 主线程提交与 Collider Cook | P95/P99 帧、单帧提交数、Cook 时间 |
| 快速移动抖动 | 任务取消、重派和淘汰 | 取消率、废弃率、队列长度 |
| Dense 高密度 | 密度 2、16 次尝试、最多 256 Piece | 布局耗时、缓存命中、拒绝数 |
| 动态碎裂 | 搜索、网格、凸分解、刚体可能同帧发生 | 体素数、碎块数、Collider 数、耗时 |
| 全场景查找 | 多处 `FindObject(s)OfType` | 热路径频次和扫描对象数 |
| 多生物 A* | 体素邻接和 Motor 同时更新 | 请求数、扩展节点、路径延迟 |
| UI 重建 | 巨型 HUD 与动态绑定 | Canvas rebuild、布局耗时、GC Alloc |

目标硬件和帧率尚未明确。建议先定义 Windows 1080p 参考硬件和目标（例如 60 FPS），再设置 P95/P99 主线程、正常流送 GC、首次可操作时间、峰值柱/Mesh/Collider 数、大爆炸碎块上限和恢复时间。数值必须由实机数据确定。

## 9. 可维护性与技术债

### 9.1 巨型类

| 文件 | 约行数 | 核心风险 |
| --- | ---: | --- |
| `MinecraftCaveInfiniteWorld.cs` | 7,465 | 生成、调度、提交、实体和生命周期耦合 |
| `GameHudController.cs` | 4,719 | 多 UI 页面、设置、流程和运行时发现耦合 |
| `JigsawStructureGenerator.cs` | 3,731 | 算法复杂，但专项测试较强 |
| `VoxelPlayerController.cs` | 3,161 | 输入、移动、视角、姿态和反馈耦合 |
| `CreatureBehaviorAgent.cs` | 1,362 | 感知、决策、移动和战斗耦合 |

拆分不能只追求行数。应先识别稳定职责与不变量，补 Characterization Tests，再进行无行为变化迁移。

### 9.2 场景服务定位

运行时多处使用 `FindObjectOfType` / `FindObjectsOfType`，涉及世界、玩家、HUD、Camera、EventSystem、任务、生物和世界 UI。Bootstrap 或场景初始化调用通常可接受，但大型控制器中的全量扫描需确认是否进入 Update 或高频路径。优先用显式序列化引用、场景上下文、注册表或窄接口处理关键依赖，不必先引入重量级 DI 框架。

### 9.3 PlayerPrefs 存档

PlayerPrefs 当前承载关卡进度、Credits、购买/升级、快捷栏、输入绑定、灵敏度、音量、全屏和新手引导。它缺少统一 Schema、版本迁移和集中 Key 管理，也不适合复杂可校验存档。建议设置项继续使用 PlayerPrefs；经济、拥有物、任务进度和快捷栏逐步迁移到 `SaveGameData + SaveGameService`。

### 9.4 编译边界

建议渐进引入：`Supernova.Core`（纯逻辑）→ `Supernova.Voxels` → `Supernova.WorldGeneration` → `Supernova.Gameplay` → `Supernova.Presentation`，再添加对应 Editor/Tests 程序集。第一阶段只抽依赖最少、测试最强的层，测量编译时间和循环依赖，不应一次拆完。

## 10. 发布配置风险

- 仓库/文档名是 Supernova，Player `productName` 是 Novacraft。
- `companyName` 仍为 `DefaultCompany`。
- `bundleVersion` 为 `0.1.2`。
- 默认分辨率为 1024×768。
- 同时启用旧输入和 Input System。
- 三档质量设置使用不同阴影距离、AA 和 VSync。
- 教程场景包含在正式 Player Build 中。

发布前应统一产品命名、公司名、版本策略、默认显示设置、图标/启动画面、输入后端、质量档默认值和目标平台，并审核第三方资源的许可证、署名与再分发限制。

## 11. AI 协作适配

### 11.1 有利条件

- `AGENTS.md` 对目录、风格、测试、Git 和 Play Mode 规则定义清晰。
- README 和 Docs 已形成事实入口与专项导航。
- Catalog、路径表、UI 层级和 Shader 名集中。
- 纯逻辑类型和测试较多，便于小步重构。
- 逐 Pass 调试场景提升 PCG 可解释性。
- 当前第一方资源 `.meta` 配对完整。

### 11.2 高风险区域

- 大型 Scene/Prefab YAML 易破坏 fileID、引用和序列化顺序。
- 巨型类难以在一次审查中覆盖完整状态机。
- `Task.Run` 路径必须严格隔离 UnityEngine 对象访问。
- Seed 确定性会受随机数调用顺序影响。
- PlayerPrefs Key、枚举序号、GUID、Socket 名是兼容契约。
- 大量未提交改动存在时必须避免覆盖用户工作。
- `Assets/3rd` 不应作为常规重构或批量删除范围。

### 11.3 推荐 AI 任务协议

每个任务应明确：

1. 玩家或编辑器可观察的目标行为。
2. 允许修改的模块、场景和资产范围。
3. Seed、GUID、存档 Key、Socket、场景入口等不变量。
4. 固定路径进入 `ProjectAssetPaths`，运行时引用进入 Catalog/配置。
5. 对应测试、场景、Seed、截图与性能指标。
6. Play Mode 状态与退出审批。
7. 资产移动保留 `.meta`，优先在 Editor 中执行。
8. 不覆盖无关未提交改动，不做全仓库格式化。

适合 AI 的任务包括补纯逻辑测试、迁移路径常量、提取无状态计算、生成配置/场景完整性测试、分析 Profiler 和更新文档。必须人工强审的任务包括场景/Prefab 大重建、随机调用顺序、体素数据布局/坐标系、多线程边界、存档迁移、Shader/平台设置、第三方删除与授权。

## 12. 风险登记表

| ID | 风险 | 概率 | 影响 | 优先级 | 动作 |
| --- | --- | --- | --- | --- | --- |
| R1 | 无本次测试与 Player Build 证据 | 高 | 高 | P0 | 建立验证与结果归档 |
| R2 | 世界/HUD/玩家巨型协调器回归 | 高 | 高 | P0 | 特征测试 + 渐进拆分 |
| R3 | 流送、Mesh、Collider 帧尖峰/内存 | 中高 | 高 | P0 | 固定性能基线与预算 |
| R4 | PlayerPrefs 无版本存档 | 中高 | 高 | P1 | Save Schema 和迁移服务 |
| R5 | 无 `.asmdef` 导致耦合与重编译扩大 | 高 | 中 | P1 | 从纯逻辑层渐进拆分 |
| R6 | 无 PlayMode 闭环自动验证 | 高 | 中高 | P1 | 5–8 条冒烟路径 |
| R7 | 全场景查找导致隐式依赖 | 中 | 中高 | P1 | 显式引用/注册，先热路径 |
| R8 | 11.1 GiB 第三方资产拖慢协作 | 高 | 中 | P1 | 依赖报告、归档、纹理预算 |
| R9 | 产品名/公司名/关卡名未收敛 | 高 | 中 | P1 | Release Checklist |
| R10 | 第三关目标低于第二关 | 中 | 中 | P2 | 策划确认 |
| R11 | Dense 结构允许交叠 | 中 | 中 | P2 | 固定 Seed 可达性验证 |
| R12 | 第三方授权清单不完整 | 未知 | 高 | P1 | 发布前授权审计 |

## 13. 分阶段路线图

### A. 1–2 周：可信基线（P0）

- 确认 Editor 非 Play Mode 后运行全量 EditMode 并保存 XML。
- 建立 Windows Development Build 冒烟清单。
- 固化三个产品场景、Catalog 和正式关卡配置测试。
- 建立 Home → Mission → 撤离/超时 → Home 的最小 PlayMode 冒烟。
- 记录固定 Seed 生成时间、可操作时间、峰值内存和 P95/P99 帧。
- 确认关卡显示名、资金曲线与第三关目标。

### B. 2–4 周：降低集中耦合（P0/P1）

- 从世界协调器提取 Generation Request/Snapshot、调度和提交职责。
- 从 HUD 提取设置、暂停、任务、Hotbar 和世界 UI。
- 从玩家控制器提取输入、运动、姿态和反馈。
- 把关键/高频全场景查找改为显式引用或注册。
- 每次只迁移一个职责，保持序列化字段和场景行为不变。

### C. 3–6 周：工程边界与存档（P1）

- 引入第一批纯逻辑/体素 `.asmdef` 和对应测试程序集。
- 建立 SaveGame Schema、版本、迁移和原子写入策略。
- 建立 CI：EditMode、资产检查、Windows Development Build。
- 为 AI/人工提交生成机器可读测试摘要。

### D. 持续：性能与资产治理（P1/P2）

- 为流送、Jigsaw、寻路和碎裂建立 Profiler 基准场景。
- 根据实测决定池化、Native 容器、Burst/Jobs 或 Mesh 优化，不预先重写。
- 生成第三方实际依赖与许可证清单，归档重复 `.unitypackage`。
- 对固定 Seed 建立结构可达性、出生安全和任务可完成性验证。

## 14. 关键决策建议

现在不建议：在无基线时重写体素/世界系统；一次性拆所有程序集；无依赖报告删除 `Assets/3rd`；继续向三个巨型控制器堆职责；一次性迁移全部 PlayerPrefs。

最值得保护的资产：体素流送与跨柱编辑不变量；Jigsaw 确定性与缓存；完整性到动态刚体链路；Catalog/路径表治理；编辑器 Builder 和逐 Pass 调试；现有测试编码的行为知识。

## 15. 结论

Supernova 的核心问题不是“技术是否足够”，而是“如何让已有技术稳定支撑持续内容生产和可发布版本”。工程拥有很强的体素/PCG 技术密度、完整玩法闭环和可观测试资产；同时，世界/HUD/玩家的集中复杂度、缺少程序集/CI/PlayMode 边界以及第三方资产规模，正在成为主要迭代成本。

建议把下一里程碑定义为“可重复验证的 Vertical Slice”：固定正式玩法路径，建立测试、构建和性能基线，再小步拆分协调器。这样既保护差异化技术，也能显著提高人工与 AI 协作安全性。

## 附录：事实来源与更新规则

- 引擎：`ProjectSettings/ProjectVersion.txt`
- 构建场景：`ProjectSettings/EditorBuildSettings.asset`
- 产品设置：`ProjectSettings/ProjectSettings.asset`
- 包依赖：`Packages/manifest.json`
- 资产入口：`Assets/Game/Runtime/Infrastructure/GameAssetCatalog.cs`
- 路径表：`Assets/Game/Editor/ProjectAssetPaths.cs`
- 关卡：`Assets/Game/Config/Levels/*.asset`
- Dense：`Assets/Game/Config/Worlds/DenseJigsawRegionWorld.asset`
- 世界：`Assets/Game/Runtime/MinecraftCaveInfiniteWorld.cs`
- 体素：`Assets/Game/Runtime/Voxels/VoxelColumnChunkData.cs`
- Jigsaw：`Assets/Game/Runtime/Structures/JigsawStructureGenerator.cs`
- 任务：`Assets/Game/Runtime/Missions/`
- 测试：`Assets/Game/Tests/Editor/`

正式场景、关卡、体素尺寸、流送范围、程序集、CI、存档、性能预算或第三方资源策略变化后应更新本报告。统计必须注明日期和口径；测试通过数只能引用同一工作区本次生成的测试结果。
