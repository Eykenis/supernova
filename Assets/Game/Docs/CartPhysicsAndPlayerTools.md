# 矿车牵引、磁力工具与玩家输入

本文档描述当前实现。Cart 矿车工具的把手牵引和 Magnet 普通刚体吸附是两套独立行为；它们只共用 `FirstPersonCartAttractor` 的物理施力辅助函数，不共享动作激活状态、输入语义或视觉表现。

## 1. 行为边界

### 1.1 矿车把手牵引

- Cart 是可购买的第 7 个快捷栏工具，商店价格为 `$250`。
- 玩家购买并选中 Cart 后，可在第一人称下点击 `CartHandle` 开始牵引。
- 矿车牵引不要求装备 Magnet；Cart 和 Magnet 分别使用 `cartTowEnabled` 与 `deviceEnabled`。
- 再次点击左键会解除当前矿车牵引。
- 开始交互时必须由准星射线直接命中把手，默认最大距离为 `2m`。
- 矿车自身的复合碰撞体不会遮挡同一刚体上的把手，但更近的墙体或其他物体仍会阻止牵引。
- 矿车牵引期间不能切换快捷栏工具。
- 矿车牵引不显示 `MagnetAttractionBeam`。

### 1.2 Magnet 普通刚体吸附

- Magnet 工具按住左键吸附准星下的普通动态 `Rigidbody`，松开后释放。
- 普通吸附默认取得距离为 `3.5m`，保持距离可在 `0.5m` 到 `6m` 之间通过滚轮调整。
- 带有 `CartHandle` 的刚体会被普通 Magnet 取得逻辑明确排除；矿车必须通过把手牵引。
- 普通吸附显示磁力光束，并允许中键配合鼠标调整物体朝向。
- 吸附期间按住右键上下拖动，会在世界 Y 轴上移动物理保持点；刚体仍只通过现有弹簧力、阻尼和最大力限制运动，不直接修改 Transform。
- Magnet 记录取得物体时的实际重心高度。物体实际升高后，最大向上力按 `baseMaximumLiftForce / (1 + liftedHeight * liftForceFalloffPerMeter)` 衰减；默认从 `300N` 开始、每米衰减系数为 `0.6`。质量和重力决定物体能否继续上升或维持高度。
- 当垂直位置弹簧达到举升上限时，速度阻尼在限幅之后继续削减上升中的力，以消耗向上的动能；物体下落时阻尼不会突破当前高度的最大举升力。这样可避免物体在 `最大举升力 = 重力` 的临界高度长期上下摆动。

`PlayerToolDefinition.animationTriggerMode` 是每个工具资产自己的表现配置，不在本文档中规定 Magnet 必须使用哪一种动画触发模式。开发者可按动画内容选择 `Single`、`Periodic` 或 `Continuous`。

## 2. 输入调用链

当前暂时保留两个左键读取入口：

1. `PlayerToolController` 根据当前选中的 `CartTool` 设置 `cartTowEnabled`。
2. `FirstPersonCartAttractor.Update()` 以 `DefaultExecutionOrder(-300)` 优先读取左键按下，处理矿车把手的开始/解除牵引。
3. `VoxelPlayerController` 生成 `PlayerInputSnapshot`，处理工具动作、角色状态机、移动和动画。
4. 如果前一步开始或解除矿车牵引，`VoxelPlayerController` 会通过 `IsTowingCart` 和当帧点击消费标记抑制工具主操作，避免同一次左键同时进入其他工具动作。
5. 未牵引矿车且当前工具允许主操作时，`VoxelPlayerController` 进入统一的 `ToolAction` 状态；Magnet 对应调用 `BeginAttraction()`、`TickAttraction()` 和 `EndAttraction()`。

矿车牵引和 Magnet 使用各自独立的激活状态与结束方法。矿车取得失败不会清空正在运行的 Magnet 动作，结束 Magnet 动作也不会释放矿车牵引。

## 3. 矿车跟随模型

开始牵引时记录两项世界空间状态：

- 把手相对玩家根节点的位置差；
- 矿车刚体当时的世界旋转。

牵引期间的目标位置为：

```csharp
desiredPosition = playerRoot.position + capturedWorldOffset;
```

因此：

- 玩家平移时，矿车保持开始牵引时的相对位移；
- 玩家转动视角或准星时，目标位置不会绕玩家旋转；
- 矿车不会主动朝向玩家或准星；
- 朝向弹簧只用于抵消碰撞和把手受力产生的旋转，维持开始牵引时记录的世界方向。

矿车保持动态刚体，不会被设为 Kinematic、不会挂到玩家层级，也不会直接修改 Transform。位置跟随仍由弹簧力、速度阻尼和真实碰撞完成。

默认相关参数：

| 参数 | 默认值 | 作用 |
|---|---:|---|
| `cartHandleAcquisitionDistance` | `2m` | 点击把手的最大交互距离 |
| `attractionForce` | `800N` | 最大位置跟随力 |
| `positionSpring` | `300` | 相对位置恢复强度 |
| `forceDamping` | `90` | 相对速度阻尼 |
| `orientationSpring` | `55` | 固定世界朝向的恢复强度 |
| `orientationDamping` | `14` | 角速度阻尼 |
| `maximumOrientationTorque` | `180` | 最大朝向修正加速度扭矩 |
| `breakDistance` | `8m` | 物理跟随失控时的安全断开距离 |

矿车位置力施加在 `CartHandle.AttachmentPoint`，目标速度取玩家 `CharacterController.velocity`。普通 Magnet 物体仍使用相机前方保持点，并额外受到最大吸附加速度限制。

## 4. 释放条件

矿车在以下情况下解除牵引：

- 牵引中再次按下左键；
- 离开第一人称；
- 矿车刚体变为 Kinematic；
- 把手相对目标位置超过 `breakDistance`；
- 玩家或吸附器组件被禁用。

牵引期间禁止切换快捷栏；禁用 Cart 工具或玩家组件会终止已经开始的矿车牵引。

## 5. 视觉表现

`MagnetAttractionBeam` 只在普通 Magnet 吸附持有目标时显示。`IsTowingCart` 为真时，光束会立即关闭。矿车车轮仍由 `PhysicalCartWheelAnimator` 根据刚体真实速度独立更新。

## 6. 任务矿车装配

`MissionGameLoop` 通过 `GameAssetCatalog.SceneLookups.AuthoredCartObjectName` 找到场景中的 `EmptyCart`，再调用 `MissionCart.ConfigureExisting()` 将其移动到任务出生结构指定位置。

矿车实体仍会随任务生成，但未购买或未选中 Cart 工具时不能牵引。Home 商店复用该 Prefab 作为商品展示，展示实例的刚体与碰撞体会被禁用。

当前 `Assets/3rd/EmptyCart.prefab` 包含：

- 一个质量为 `65` 的动态 `Rigidbody`；
- 复合碰撞体；
- `Tow Handle Interaction` 节点及 `CartHandle`；
- `PhysicalCartWheelAnimator`。

`MissionCart` 还会保证货舱存在 `CartCargoValueZone`。矿石处于该触发区期间，不会因车内碰撞损失价值。

## 7. 相关代码

- `Assets/Game/Runtime/Gameplay/FirstPersonCartAttractor.cs`
- `Assets/Game/Runtime/Gameplay/CartHandle.cs`
- `Assets/Game/Runtime/Effects/MagnetAttractionBeam.cs`
- `Assets/Game/Runtime/Gameplay/PlayerToolController.cs`
- `Assets/Game/Runtime/Voxels/VoxelPlayerController.cs`
- `Assets/Game/Runtime/Missions/MissionCart.cs`
- `Assets/Game/Runtime/Missions/MissionGameLoop.cs`
- `Assets/Game/Runtime/Missions/CartCargoValueZone.cs`
- `Assets/Game/Config/Tools/CartTool.asset`
- `Assets/Game/Config/Shop/CartProduct.asset`

对应 EditMode 测试位于：

- `Assets/Game/Tests/Editor/FirstPersonCartAttractorTests.cs`
- `Assets/Game/Tests/Editor/WorldAndEffectTests.cs`
- `Assets/Game/Tests/Editor/CharacterCombatStateMachineTests.cs`
