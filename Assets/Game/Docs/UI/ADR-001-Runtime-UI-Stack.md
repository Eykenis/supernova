# ADR-001：运行时 UI 统一采用 UGUI + TextMeshPro

- 状态：Accepted
- 日期：2026-07-26
- 适用版本：Unity 2022.3.62t11 / Tuanjie 1.9.3

## Context

项目同时使用 UI Toolkit、UGUI/TMP 和 IMGUI。主菜单只有一个 UIDocument，而 HUD、Crosshair、Pause、Loading 和相应 EditMode 测试主要围绕 UGUI/TMP 建立。项目还需要第一人称 HUD、潜在世界空间提示、定制材质/VFX、MonoBehaviour 直接引用和可编辑 Prefab。

Unity 2022.3 官方文档将 UGUI 列为运行时推荐方案，将 UI Toolkit 列为替代方案；该版本 UI Toolkit 不具备世界空间运行时 UI、定制材质/Shader、序列化事件和 Timeline 集成等能力。

## Decision

1. 正式运行时 UI 统一使用 UGUI + TextMeshPro。
2. 通过 ScriptableObject Design Tokens 补足 UGUI 缺少全局样式系统的问题。
3. 通过 Prefab/Prefab Variant 作为可视化配置和复用边界。
4. 采用 View/Presenter/Controller/Coordinator 分层，禁止 Controller 创建完整视觉树。
5. UI Toolkit 用于 Editor 工具；现有主菜单 UI Toolkit 只在迁移期作为回退。
6. Runtime IMGUI 迁移到 UGUI Diagnostics Overlay，最终仅允许 Editor/Development Build 调试路径。
7. 当前不引入 NoesisGUI、FairyGUI 或 Gameface。

## Consequences

正面：

- 最大化复用现有 HUD、TMP、EventSystem、Presenter 和测试。
- 与 Unity/Tuanjie 2022.3 功能边界匹配。
- 支持世界空间、材质/Shader、动画和序列化引用。
- 迁移可以按屏幕进行，不需要一次重写全部 UI。

代价：

- UGUI 没有 USS/CSS 式全局样式，必须严格使用 Design Tokens 和可复用组件。
- 大型列表和复杂数据界面需要额外 Presenter/虚拟化策略。
- 需要主动管理 Canvas 拆分和脏更新，避免重建与批次开销。

## Revisit Triggers

满足以下任一条件时重新评估 NoesisGUI/Gameface/UI Toolkit：

- 升级到 Unity/Tuanjie 新版且 UI Toolkit 具备项目所需的世界空间、Shader 和输入能力。
- UI 页面数量或复杂数据界面规模显著增长，Prefab 工作流成为主要瓶颈。
- 建立独立 Web/XAML UI 团队，需要与 Unity 玩法开发并行交付。
- 主机平台、跨引擎复用或认证需求足以抵消商业中间件成本。

