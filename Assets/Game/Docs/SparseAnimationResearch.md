# 少骨骼 / 少关键帧 3D 动画方案调研与 Player 实验

日期：2026-07-22

## 结论

针对本项目的 `MinecraftCaves/Prefabs/Player.prefab`，最合适的短期方案是：

1. 用少量主骨骼定义 4–5 个关键姿态；
2. 将姿态转换为 Unity Humanoid muscle 曲线；
3. 使用平滑曲线自动生成中间帧；
4. 后续如需要脚底锁定、双手握持、命中点约束，再叠加 Animation Rigging / IK。

这样无需引入推理模型或外部运行时，动画能直接进入现有 Animator Controller，也可以重复生成和调整。

## 方案比较

| 方案 | 输入量 | 优势 | 限制 | 本项目适用性 |
|---|---:|---|---|---|
| 稀疏姿态 + 四元数/Humanoid 曲线插值 | 约 10–15 根主骨，4–5 帧 | 可控、轻量、直接进入 Unity | 复杂接触需要 IK 修正 | **当前采用** |
| Unity Animation Rigging / IK | 手脚/身体少量控制器 | 适合脚贴地、双手握镐、瞄准和交互 | 当前项目未安装该包；需要搭建 Rig | **适合作为第二阶段** |
| Cascadeur AutoPosing + Inbetweening + AutoPhysics | 每个姿态只动少量控制器 | 自动补全全身姿势、重心和物理感 | 外部 DCC 流程，需要导入导出和重新绑定 | **最合适的外部美术方案** |
| Diffusion motion in-betweening | 稀疏全身/局部关键帧，可附带文本 | 能生成更自然且多样的过渡 | 模型接入、骨架映射、推理成本与确定性问题 | 研究/离线生产可用，当前不宜直接集成 |
| Motion Matching | 轨迹和状态 | 游戏运行时自然连续 | 需要大量动作数据库，不符合“极少关键帧” | 不适合本目标 |
| 物理布娃娃 + 烘焙 | 初始冲量和关节参数 | 击倒自然 | 奔跑和挖矿不适用，结果难精确控制 | 可专用于击倒增强 |

## 参考依据

- Cascadeur AutoPosing：只操作少量控制器，系统预测全身姿态；需要标准 Cascadeur rig。
  - https://cascadeur.com/help/tools/animation_tools/autoposing
  - https://cascadeur.com/help/getting_started/workflow_basics
- Cascadeur 将 AutoPosing、Inbetweening、AutoInterpolation 列为机器学习工具，AutoPhysics 为非 AI 物理工具。
  - https://cascadeur.com/help/category/285
- Unity Animation Rigging 提供基于约束的程序化运动、IK、Aim、附件和世界交互修正。
  - https://docs.unity3d.com/Packages/com.unity.animation.rigging%401.4/manual/RiggingWorkflow.html
- NVIDIA CondMDI 展示任意稀疏/密集关键帧和局部约束下的动作补间。
  - https://research.nvidia.com/publication/2024-05_flexible-motion-betweening-diffusion-models
- NVIDIA Kimodo 支持稀疏关节位置/旋转、全身关键帧、端点约束和根轨迹，但属于研究级生成模型接入路线。
  - https://research.nvidia.com/labs/sil/projects/kimodo/

## Player 骨骼与实验

Player 使用有效的 Humanoid Avatar。虽然模型有约 70 个骨骼/节点，实验只显式控制主要身体骨骼：

- Hips / Spine / Chest / Head
- 左右 UpperArm / LowerArm
- 左右 UpperLeg / LowerLeg / Foot

生成器把这些少量骨骼姿态转换为 Humanoid muscle 曲线，因此 Animator 可以正确播放，而不是直接使用会被 Humanoid 求解器覆盖的原始 Transform 曲线。

### 已生成动作

| 动作 | 稀疏姿态数 | 主要受控骨骼 | 长度 |
|---|---:|---:|---:|
| 奔跑 `SparseRun.anim` | 4 个独立姿态 + 1 个闭环姿态 | 13 | 0.48 秒，循环 |
| 挖矿 `SparseMine.anim` | 5 | 右臂、左臂辅助、躯干、根 | 0.82 秒 |
| 被击倒 `SparseKnockdown.anim` | 5 | 躯干、四肢、根 | 1.25 秒，末帧保持倒地 |

### 操作

- 移动时：原 Animator 的 `Walk` 状态已替换为 `SparseRun`。
- 鼠标左键：播放挖矿。
- K：播放被击倒并保持倒地姿势。
- R：恢复到 Idle。

### 资产

- 生成工具：`Assets/Game/Editor/Animation/SparsePlayerAnimationGenerator.cs`
- 运行时动画输入与状态触发：`Assets/Game/Runtime/Voxels/VoxelPlayerController.cs`
- 动画：`Assets/Game/Animations/Generated/`
- 相关动画契约测试：`Assets/Game/Tests/Editor/FirstPersonAnimationControllerTests.cs`
- 菜单：`Tools/Supernova/Animation/Generate Sparse Player Animations`

## 后续建议

1. 安装 Animation Rigging，在右手/左手增加 Two Bone IK，让双手稳定握住镐柄。
2. 为奔跑增加 Foot IK 和地面射线，消除脚滑和悬空。
3. 被击倒前半段保持关键帧，落地后切换短时 ragdoll，再烘焙或混合回动画。
4. 如果动画制作量快速增加，使用 Cascadeur 作为离线姿态和物理补间工具，再导回 Unity。
