# Supernova 文档导航

本目录同时保存当前实现说明、配置手册、架构决策和研究资料。文档标题或正文中标为“调研”“目标”“PRD”“建议”“后续”的内容不代表功能已经落地；发生冲突时，以当前场景、配置资产、运行时代码和本次测试结果为准。

## 工程总览

- [工程 AI 分析报告](工程AI分析报告.md)：当前产品闭环、架构、质量、风险、AI 协作建议与治理路线。

## 当前运行时与数据链路

- [MinecraftCaves 地形生成运行时说明](MinecraftCaves世界生成与Voxel依赖.md)：关卡配置、洞穴/超平坦模式、Dense Jigsaw 覆盖、柱流送、网格和出生流程。
- [Minecraft 风格洞穴与无限区块生成](Minecraft洞穴与无限区块生成.md)：噪声、密度与无限流送算法背景。
- [体素与距离场生成](体素与距离场生成.md)：体素容器、密度、网格和编辑接口。
- [Minecraft 矿物生成与项目体素链路](Minecraft矿物生成与项目体素链路.md)：五类矿物 Feature、跨柱确定性和采掘后刚体。
- [Fixed Voxel Structures](FixedVoxelStructures.md)：固定结构创作、运行时编辑和 SpawnShelter 落地顺序。
- [生物行为状态](生物和行为树.md)：怪物状态机、体素 A*、移动与卡住恢复。

## Jigsaw 结构

- [Jigsaw 结构配置手册](Jigsaw结构配置手册.md)：策划/美术配置新结构的首选入口。
- [Jigsaw 结构生成算法](Jigsaw结构生成.md)：项目当前选址、布局、落地、缓存与明确未实现项。
- [拼接结构生成与编辑](JigsawStructureGeneration.md)：较短的架构与资源入口说明。
- [Minecraft 生成式结构算法指南](Guideline/Minecraft生成式结构算法指南.md)：原版机制调研、通用模型与项目映射。

## 渲染、动画、输入与 UI

- [洞穴草地渲染](洞穴草地渲染.md)：实例化草地、Forward 附加光、LOD 与主相机限制。
- [Point / Spot 柔化衰减](SoftFalloffLighting.md)：洞穴非晶体材质的柔化附加光曲线。
- [Noise Tessellation](Noise_Tessellation.md)：噪声细分渲染试验记录。
- [第一人称角色与 Muryotaisu 动画](FIRST_PERSON_ANIMATION.md)：当前第一/第三人称动画契约。
- [稀疏姿态动画调研](SparseAnimationResearch.md)：历史实验与仍可运行的生成器说明；生成资产未签入仓库。
- [输入按键文本转义](输入按键文本转义.md)：`{{input:...}}` 语法与运行时替换。
- [ADR-001：运行时 UI 技术栈](UI/ADR-001-Runtime-UI-Stack.md)：UGUI + TextMeshPro 决策。
- [UI 审计与重构 PRD](UI/UI_AUDIT_AND_REFACTOR_PRD.md)：保留历史基线，并在开头记录当前同步状态。

## 调研与路线参考

- [深岩银河技术借鉴目标](DRG_TECH_GOALS.md)
- [Minecraft 寻路调查](Minecraft寻路.md)
- `Assets/Game/Research/`：体素连通性、支撑、应力和物理破坏的研究与原型报告。

## 维护规则

1. 文档引用项目资产时使用仓库相对路径，并在资源移动后同步更新。
2. 易变化的测试数量、通过率和耗时不写成长期事实；引用本次生成的测试报告。
3. 当前入口以 `ProjectSettings/EditorBuildSettings.asset` 为准；当前关卡/资源以 `GameAssetCatalog.asset` 与 `LevelConfiguration` 为准。
4. 编辑器工具的资产路径以 `Assets/Game/Editor/ProjectAssetPaths.cs` 为唯一集中表，不在文档示例中鼓励新增散落硬编码。
