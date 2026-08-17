# Supernova UI/GUI 统一与重构需求文档

- 文档状态：保留 2026-07-26 评审基线；下方另列当前实现同步
- 基线日期：2026-07-26
- 最近同步：2026-08-17
- 项目版本：Unity 2022.3.62t11 / Tuanjie 1.9.3
- 目标平台：Standalone Windows 64-bit
- 决策：运行时统一采用 UGUI + TextMeshPro；UI Toolkit 仅保留为迁移回退和编辑器工具方案；IMGUI 退出发布版运行时 UI

## 0. 2026-08-17 当前实现同步

- 产品流程不再使用独立 MainMenu 场景：`Home.scene` 内含可编辑的
  `Assets/UI/UI/MainMenuCanvas.prefab`、角色展示和进入第一人称的镜头过渡。
- `MainMenuController` 已优先使用 UGUI `MainMenuView`；`Assets/UI/MainMenu/` 的
  UI Toolkit 资产和 Controller 回退分支仍保留，因此 Phase 4 尚未完成。
- `UiDesignTokens`、`UiCanvasPolicy`、`UiSafeArea`、Sci-Fi 皮肤、统一 EventSystem、
  输入重绑、装备菜单和暂停肖像均已落地，并有 EditMode 覆盖。
- HUD、任务、加载、暂停和部分装备视图仍由 `GameHudController` 运行时构建；
  Phase 2 的“Prefab 成为唯一真源”和职责拆分尚未完成。
- 运行时仍存在受调试/示例用途驱动的 `OnGUI()`（例如 Gallery、Portal Example、
  体素完整性实验与结构编辑器）；Phase 3 不能标记为完成。
- 当前 Build Settings 顺序为 `Home` → `DenseJigsawRegion` →
  `SpawnShelterStoneTest`，其中最后一项是教程入口。

## 1. 背景与目标

项目当前同时使用 UI Toolkit、UGUI/TMP 和 IMGUI。三套系统分别承担主菜单、游戏 HUD/暂停/加载、运行时诊断信息，导致视觉规范、缩放策略、生命周期、输入模型和测试方式不一致。

本需求的目标是：

1. 形成可追踪的 UI 使用基线和问题清单。
2. 建立一个高清、可配置、可扩展、适配 Unity/Tuanjie 2022.3 的运行时 UI 方案。
3. 通过逐屏迁移统一主菜单、HUD、暂停、加载和诊断界面。
4. 保证迁移期间可回退，不中断玩法开发。

## 2. 2026-07-26 使用基线（历史快照）

本节保留立项时的证据和问题编号，不能再当作当前资产清单；当前状态以第 0 节为准。

### 2.1 技术分布

| 区域 | 当前技术 | 主要资产/代码 | 状态 |
| --- | --- | --- | --- |
| 主菜单与设置 | UI Toolkit / UIDocument | `Assets/UI/MainMenu/MainMenu.uxml`、`MainMenu.uss`、`MainMenuPanelSettings.asset`、`MainMenuController.cs` | 已有界面，正在迁移 |
| 游戏 HUD | UGUI + TextMeshPro | `GameHudController.cs`、`InfiniteCaves.scene` | 运行中，视图和控制器耦合 |
| 暂停菜单 | UGUI + TextMeshPro | `GameHudController.cs`、`InfiniteCaves.scene` | 运行中 |
| 加载界面 | 运行时代码生成的 UGUI + TextMeshPro | `GameHudController.cs` | 运行中，不便编辑 |
| 世界生成状态 | IMGUI | `MinecraftCaveInfiniteWorld.OnGUI()` | 发布版仍显示 |
| 洞穴画廊控制与标注 | IMGUI | `MinecraftCaveGallery.OnGUI()` | 运行中 |
| 编辑器扩展 | IMGUI EditorGUILayout | `Assets/Game/Editor/**` | 合理，可继续保留 |

量化结果：

- 运行时 UI Toolkit：1 个 UXML、1 个 USS、1 个 PanelSettings、1 个 UIDocument 控制器。
- `InfiniteCaves` 序列化场景：HUD、Crosshair、Pause 三个 Canvas；Loading Canvas 由代码补建。
- 游戏侧 UGUI/TMP 核心控制器：`GameHudController.cs` 约 1061 行。
- 运行时 IMGUI：2 个 `OnGUI()` 实现。
- UI 相关 EditMode 基线测试：原有 `GameHudControllerTests`，本轮新增 3 个 UI 基础测试。

### 2.2 已确认缺陷

| 编号 | 严重度 | 问题 | 证据/影响 |
| --- | --- | --- | --- |
| UI-001 | P0 | 主菜单 UXML 曾因乱码和缺失引号而不是合法 XML | `UNMAPPED DEPTH` 数值与 Footer 分隔符损坏；已修复为 ASCII 安全内容 |
| UI-002 | P0 | 主菜单运行截图出现游戏准星和快捷栏 | 页面生命周期与常驻 HUD 激活策略不可靠；菜单信息层级被破坏 |
| UI-003 | P1 | 三套运行时 UI 技术并存 | 样式、输入、测试、状态切换和维护成本重复 |
| UI-004 | P1 | Canvas 缩放策略不一致 | 场景 HUD Canvas 为 Constant Pixel Size，而代码回退路径配置为 1920×1080 Scale With Screen Size；Crosshair 使用 Constant Pixel Size 属于合理特例 |
| UI-005 | P1 | 视觉令牌散落 | 主菜单使用深色/橙/青；HUD 又使用青/绿/黄；大量颜色、字号、排序层直接写在脚本中 |
| UI-006 | P1 | `GameHudController` 兼任生命周期、视图构建、数据绑定、暂停、加载、输入和样式 | 约 1061 行，修改任一界面都可能影响其他界面 |
| UI-007 | P1 | 发布运行时使用 IMGUI 固定像素布局 | 不响应安全区和分辨率，无法复用正式视觉规范，且 `OnGUI()` 每帧执行 |
| UI-008 | P1 | 主菜单最小字号过小 | 原 USS 大量使用 8–12 px；在高 DPI、手柄距离和缩小 Game View 下可读性不足 |
| UI-009 | P2 | 字符串硬编码且无本地化入口 | 英文文案分散在 UXML 和 C#；无法安全接入中文、回退字体和文本扩展 |
| UI-010 | P2 | 设置没有统一数据模型 | 原主菜单直接写 `Screen.fullScreen` 与 `AudioListener.volume`，无持久化；第一阶段已增加 PlayerPrefs 迁移实现 |
| UI-011 | P2 | 输入焦点策略不统一 | UI Toolkit 与 UGUI 各自处理事件；Gamepad/Keyboard 初始焦点和返回路径没有统一验收 |
| UI-012 | P2 | 自动生成视图不便美术配置 | HUD/Loading/Pause 部分依赖运行时代码创建，Prefab 不是唯一真源 |

### 2.3 现有优势

- UGUI、TextMeshPro 和 EventSystem 已安装且已有测试覆盖。
- HUD 已有 Presenter 雏形，数据源和视图可以继续拆分。
- 主菜单已经形成明确的“深空洞穴 + 工业橙色”视觉方向，可迁移而无需推翻品牌。
- 当时计划使用 MainMenu → InfiniteCaves；当前实际入口已改为第 0 节所列的 Home → DenseJigsawRegion → Tutorial。

## 3. 技术调研与选型

### 3.1 候选方案

| 方案 | 高清能力 | 配置工作流 | 扩展能力 | 2022.3 项目适配 | 迁移成本 | 主要风险 |
| --- | --- | --- | --- | --- | --- | --- |
| UGUI + TextMeshPro + Prefab + Design Tokens | TMP SDF 文本；Sprite/9-slice；可接材质和 Shader | Unity Inspector、Prefab Variant、Scene View | MonoBehaviour、Animator、Timeline、世界空间、材质 | 很高；项目已安装并大量使用 | 低 | 原生缺少全局 CSS，必须用令牌和组件规范补齐 |
| UI Toolkit Runtime | 矢量式无纹理元素、抗锯齿、动态图集 | UXML/USS/UI Builder | 样式复用强，适合大量 Overlay UI | 中；2022.3 官方仍将其列为运行时替代方案 | 中 | 2022.3 缺少世界空间、定制材质/Shader、序列化事件和 Timeline 集成 |
| NoesisGUI | 矢量、可变字体、XAML、分辨率独立 | XAML、Noesis Studio、MVVM | 很高，适合复杂/跨平台 UI | 官方列出 Unity 2020.2–2023.x 兼容 | 高 | 商业中间件、许可、原生插件、团队需学习 XAML/MVVM |
| FairyGUI | 独立编辑器、运行时组件、虚拟列表等 | FairyGUI Editor | 中高 | 官方宣称支持 Unity 全版本 | 中高 | 外部工具链、导出资源、运行时框架锁定、现有 UGUI 资产复用差 |
| Coherent Gameface | HTML/CSS 前端工作流，高复杂度 UI | Web 工具链 | 很高 | 有 Unity 集成 | 很高 | 商业许可、专有运行时、包体与集成复杂度，不匹配当前项目规模 |

### 3.2 Unity 2022.3 约束

Unity 2022.3 官方 UI 对比文档给出的运行时首选仍是 Unity UI（UGUI），UI Toolkit 是替代方案。该版本中：

- UGUI 支持世界空间 UI、定制材质与 Shader、序列化事件、Animation Clip 和 Timeline。
- UI Toolkit 更适合大量、多分辨率的屏幕 Overlay，并有全局样式和无纹理元素优势。
- UI Toolkit 2022.3 不支持世界空间运行时 UI和定制材质/Shader，也不支持序列化事件与 Timeline 集成。
- TextMeshPro 为 UGUI 提供可缩放文本、Rich Text 和字体回退能力。

本项目有第一人称 HUD、准星、潜在世界空间交互提示、URP/VFX 需求，同时现有 UGUI/TMP 投资明显高于 UI Toolkit。因此不应为了主菜单的一套 UXML 把其余运行时 UI 全部迁移到 UI Toolkit。

### 3.3 选型结论

运行时统一采用：

> UGUI + TextMeshPro + Prefab/Prefab Variant + ScriptableObject Design Tokens + Presenter/View 分层

边界规定：

- Runtime 正式 UI：UGUI + TMP。
- Editor 工具：优先 UI Toolkit，已有简单 IMGUI Editor 可按需保留。
- Runtime IMGUI：只允许 `DEVELOPMENT_BUILD || UNITY_EDITOR` 的临时诊断；最终迁移到 UGUI Diagnostics Overlay。
- UI Toolkit 主菜单：第一阶段作为回退保留；UGUI 主菜单通过 `MainMenuController` 优先加载。验收完成后删除 UIDocument/UXML/USS/PanelSettings。
- 第三方中间件：当前不引入。若未来出现大量复杂数据 UI、跨引擎 UI 团队或主机认证需求，再用垂直切片重新评估 NoesisGUI/Gameface。

## 4. 产品与工程需求

### 4.1 统一视觉系统

#### UI-R-001 Design Tokens

所有新运行时 UI 必须从 `UiDesignTokens` 读取以下令牌：

- 参考分辨率、宽高匹配值、Pixels Per Unit。
- Backdrop、Surface、Raised Surface、Primary/Secondary Text。
- Accent、Focus、Success、Divider。
- Display、Body、Control、Caption 字号。
- Quick/Screen Transition 时长。

禁止新增“无解释”的独立颜色、字号和过渡时间。确需例外时，应在组件字段中标明用途。

#### UI-R-002 品牌风格

- 保留深色洞穴背景、工业橙主操作、青色状态/焦点的视觉语言。
- 主操作必须在亮度和色相上明显高于普通操作。
- 正文与功能按钮在 1920×1080 参考画布下不得小于 18 pt；Caption 不得小于 14 pt。
- 文本优先使用 TMP SDF 字体，不将文字烘焙进位图。

### 4.2 分辨率与高清

#### UI-R-003 画布策略

- 正式 Screen Space Canvas 默认使用 `Scale With Screen Size`。
- 统一参考分辨率 1920×1080，默认 Match Width Or Height = 0.5。
- Crosshair 可以使用 Constant Pixel Size，但必须单独 Canvas 且注明例外。
- 所有可交互屏幕必须包含 `UiSafeArea` 根节点。

#### UI-R-004 适配矩阵

每个屏幕至少验证：

- 1280×720、1920×1080、2560×1440。
- 1920×1200（16:10）。
- 2560×1080 或 3440×1440（21:9）。
- 1024×768（4:3，允许布局压缩但不得遮挡主操作）。
- Windows 100%、150%、200% 显示缩放下文本仍清晰。

### 4.3 架构

#### UI-R-005 Prefab 为视图真源

- MainMenu、GameHud、Pause、Loading、Diagnostics 分别为独立 Prefab。
- 运行时只实例化 Prefab，不在 Controller 中逐个创建视觉节点。
- 允许 Editor Builder 生成/升级 Prefab，但生成后资源必须可在 Inspector 中直接调整。

#### UI-R-006 View/Presenter/Coordinator 分层

- View：仅保存组件引用和简单显隐/显示方法。
- Presenter：接收纯数据并刷新 View，不查找场景对象。
- Controller：订阅输入和业务事件，不创建样式。
- `UiSceneCoordinator`：唯一负责场景切换、常驻 UI、Sorting Order、输入焦点和 HUD 生命周期。

`GameHudController` 应拆分为：

- `GameHudView` + `GameHudPresenter`
- `HotbarView` + `HotbarPresenter`
- `PauseMenuController`
- `LoadingScreenController`
- `UiSceneCoordinator`

#### UI-R-007 生命周期

- MainMenu 场景不得显示 Gameplay HUD、Crosshair、Pause 或 Loading。
- Gameplay 场景不得保留 MainMenu Canvas/UIDocument。
- 同类根视图同时最多存在一个。
- 不再依赖 `Resources.FindObjectsOfTypeAll` 选择任意候选对象。

### 4.4 输入、可访问性与设置

#### UI-R-008 输入

- Mouse、Keyboard、Gamepad 使用同一导航图。
- 打开屏幕后必须设置确定的初始选择项。
- Escape/B 必须返回上一级，顶层 Gameplay 中打开/关闭 Pause。
- 隐藏面板不得保留焦点或接收 Raycast。

#### UI-R-009 设置

- Fullscreen 和 Master Volume 必须持久化。
- 设置修改应写入统一 `GameSettings` 数据模型；第一阶段 PlayerPrefs 仅为迁移实现。
- 后续增加分辨率、显示模式、音频分类和输入灵敏度时，不修改具体 View 的存储逻辑。

#### UI-R-010 本地化

- 可见文案不得长期硬编码在 Controller。
- 文本键、英文默认值和字体回退策略应进入统一本地化层。
- 中文验证必须覆盖字形、换行、截断和控件扩展。

### 4.5 性能与质量

#### UI-R-011 性能

- 静态文本/图像不在 `Update()` 中重复写值。
- UI 刷新采用事件或脏标记；禁止整棵 UI 每帧重建。
- IMGUI 发布运行时路径必须移除。
- 画布拆分以更新频率为依据：HUD 动态层、静态装饰、Modal Overlay 分离。
- 迁移完成后用 Profiler 记录 UI CPU、Batches、Vertices 和 GC Alloc 基线。

#### UI-R-012 自动化

- Design Tokens 和 Canvas Policy 必须有 EditMode 测试。
- 每个 Prefab 必须有引用完整性测试。
- MainMenu → Gameplay → Pause → Resume → MainMenu 必须有 PlayMode 流程测试。
- 分辨率矩阵至少生成截图进行人工或图像基线复核。

## 5. 分阶段执行计划

### Phase 0：审查与止血（本轮已完成）

- 盘点三套 UI 技术和场景/脚本分布。
- 修复 MainMenu UXML 非法 XML/乱码。
- 记录原有 EditMode 基线：71 项，68 通过、3 项失败；失败与 UI 无关。
- 输出本 PRD 与技术决策记录。

验收：项目无新增编译错误；UXML 可被 XML 解析器读取。

### Phase 1：基础设施与主菜单垂直切片（本轮已完成第一版）

- 新增 `UiDesignTokens`。
- 新增 `UiCanvasPolicy` 和 `UiSafeArea`。
- 新增可编辑 `MainMenuCanvas.prefab`，使用 UGUI + TMP。
- `MainMenuController` 优先加载 UGUI Prefab，缺失时回退 UI Toolkit。
- Fullscreen 与 Master Volume 增加持久化。
- 新增 3 个 UI 基础 EditMode 测试并全部通过。
- 生成运行时截图 `Assets/Screenshots/MainMenuUguiPhase1.png`。

退出条件：

- 视觉和交互经过产品确认。
- 720p、1080p、1440p、16:10、21:9、4:3 截图通过。
- UGUI 设置页、按钮焦点和场景跳转完成 PlayMode 验证。

### Phase 2：HUD、暂停、加载迁移

- 以 `Assets/UI/UI/Game HUD.prefab` 和 `DenseJigsawRegion.scene` 为当前正式链路，继续减少场景与运行时代码的重复 UI 层级。
- 以 `UiDesignTokens` 替换脚本硬编码颜色和字号。
- Root HUD Canvas 改为统一 Policy；Crosshair 保留 Constant Pixel Size 例外。
- Loading/Pause 从代码生成改为 Prefab。
- 拆分 `GameHudController` 职责，保留当前 public API 的兼容 Adapter。

退出条件：现有 `GameHudControllerTests` 通过；视觉流程与旧版功能一致；无重复 EventSystem/Canvas。

### Phase 3：IMGUI 清理与诊断界面

- 把 `MinecraftCaveInfiniteWorld.OnGUI()` 数据映射到 Diagnostics Presenter。
- 把 `MinecraftCaveGallery.OnGUI()` 的 Seed 控制和标签迁移到 UGUI Diagnostics/View。
- 发布构建默认关闭 Diagnostics；Editor/Development Build 可通过热键开启。

退出条件：Runtime 源码中不存在未受编译条件保护的 `OnGUI()`。

### Phase 4：移除遗留 UI Toolkit 与完善体验

- MainMenu 验收后从场景删除 UIDocument。
- 删除 `Assets/UI/MainMenu` 的 UXML、USS 和 PanelSettings。
- Controller 移除 UI Toolkit 回退代码。
- 接入统一本地化与 TMP 字体回退。
- 完成 Gamepad 导航、Focus 状态、返回路径和可访问性审查。

### Phase 5：全链路验收

- PlayMode 自动化：MainMenu → Gameplay → Loading → HUD → Pause/Resume。
- 全分辨率截图矩阵。
- Profiler 基线与长时间场景切换检查。
- 清理 Runtime Resources 过渡加载，改为显式 Prefab 引用或后续 Addressables 策略。

## 6. 本轮实施清单

| 状态 | 资产 |
| --- | --- |
| 已完成 | `Assets/Game/Runtime/UI/UiDesignTokens.cs` |
| 已完成 | `Assets/Game/Runtime/UI/UiCanvasPolicy.cs` |
| 已完成 | `Assets/Game/Runtime/UI/UiSafeArea.cs` |
| 已完成 | `Assets/Game/Runtime/UI/MainMenuView.cs` |
| 已完成 | `Assets/Game/Runtime/UI/MainMenuController.cs` 双栈迁移控制器 |
| 已完成 | `Assets/Game/Editor/UI/MainMenuUguiPrefabBuilder.cs` |
| 已生成 | `Assets/Game/Config/UI/DefaultUiDesignTokens.asset` |
| 已生成 | `Assets/UI/UI/MainMenuCanvas.prefab` |
| 已完成 | `Assets/Game/Tests/Editor/UiFoundationTests.cs` |
| 已修复 | `Assets/UI/MainMenu/MainMenu.uxml` |

## 7. 风险与回退

- 现有工作区有大量未提交玩法与第三方资源改动。本轮没有清理、重置或覆盖无关改动。
- MainMenu 采用双栈过渡：UGUI Prefab 找不到时继续启用 UIDocument，避免一次切换导致入口不可用。
- UI Toolkit 资源在 Phase 4 前不删除。
- HUD 大文件只在 Phase 2 按小步拆分；每一步保持现有测试和公开 API。
- 第三方 UI 中间件不在当前阶段引入，避免把 UI 重构与许可、原生插件、包管理风险绑定。

## 8. 参考资料

- Unity 2022.3 官方 UI 系统对比：<https://docs.unity3d.com/cn/2022.3/Manual/UI-system-compare.html>
- NoesisGUI 技术与功能：<https://www.noesisengine.com/noesisgui/>
- NoesisGUI Unity 集成与兼容性：<https://www.noesisengine.com/docs/Gui.Core.Unity3DTutorial.html>
- FairyGUI 官方下载与 Unity Runtime：<https://www.fairygui.com/download>
- Coherent Gameface Unity 概览：<https://docs.coherent-labs.com/unity-gameface/what_is_gfp/overview/>
