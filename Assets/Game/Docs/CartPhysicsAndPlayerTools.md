# 矿车牵引、磁力工具与玩家输入

本文档描述当前实现。Cart 矿车工具的把手牵引和磁力吸附是两套独立行为；它们只共用 `FirstPersonCartAttractor` 的物理施力辅助函数，不共享动作激活状态、输入语义或视觉表现。

磁力吸附不再是独立工具，而是**固定绑定在右键**上：无论手上拿什么工具（包括空手），右键都是磁铁。镐子的投掷改用独立按键 `PlayerProfile.throwPickaxeKey`（默认 `G`）。

## 1. 行为边界

### 1.1 矿车把手牵引

- Cart 是可购买的第 7 个快捷栏工具，商店价格为 `$250`。
- 玩家购买并选中 Cart 后，可在第一人称下点击 `CartHandle` 开始牵引。
- 矿车牵引不要求装备磁铁；Cart 和磁铁分别使用 `cartTowEnabled` 与 `deviceEnabled`。
- 再次点击左键会解除当前矿车牵引。
- 开始交互时必须由准星射线直接命中把手，默认最大距离为 `2m`。
- 矿车自身的复合碰撞体不会遮挡同一刚体上的把手，但更近的墙体或其他物体仍会阻止牵引。
- 矿车牵引期间不能切换快捷栏工具。
- 矿车牵引不显示 `MagnetAttractionBeam`。

### 1.2 磁力吸附（右键，所有工具通用）

- 按住右键吸附准星下的普通动态 `Rigidbody`，松开后释放。
- 普通吸附默认取得距离为 `3.5m`，保持距离可在 `0.5m` 到 `6m` 之间通过滚轮调整。
- **保持距离在每次取得时重置为物体当时的实际距离**（`CalculateInitialHoldDistance`），而不是沿用上一次抓取结束时的值。否则刚抓住的瞬间物体会被猛地拉近或推远。
- 带有 `CartHandle` 的刚体会被普通取得逻辑明确排除；矿车必须通过把手牵引。
- 带有 `ThrownPickaxe` 的刚体也被排除，改走 1.3 的牵引路径。
- 吸附显示磁力光束，并允许中键配合鼠标调整物体朝向。
- 当右键确实能牵引到东西时（普通刚体或已抛出的镐子），准星高亮：变为饱和橙金 `(1, 0.55, 0)`、放大到 `1.6x`、并把原本的黑色描边渐变为同色光晕。准星只有 2px 粗，单靠色相在明亮地形上几乎看不出差异，所以必须同时改变尺寸。状态切换用 `Mathf.MoveTowards` 平滑（`CrosshairStateBlendSpeed = 14`），避免目标短暂离开准星时闪烁。由 `FirstPersonCartAttractor.HasAvailableMagnetTarget()` 驱动，`GameHudController` 每帧刷新。
- 因为右键已被吸附本身占用，原先的"右键 + 鼠标 Y 调整保持点高度"已删除。`baseMaximumLiftForce` 仍然生效。
- 磁铁记录取得物体时的实际重心高度。物体实际升高后，最大向上力按 `baseMaximumLiftForce / (1 + liftedHeight * liftForceFalloffPerMeter)` 衰减；默认从 `300N` 开始、每米衰减系数为 `0.6`。质量和重力决定物体能否继续上升或维持高度。
- 当垂直位置弹簧达到举升上限时，速度阻尼在限幅之后继续削减上升中的力，以消耗向上的动能；物体下落时阻尼不会突破当前高度的最大举升力。这样可避免物体在 `最大举升力 = 重力` 的临界高度长期上下摆动。
- 吸附期间播放 `PlayerToolDefinition.magnetHoldAnimation`（循环的双手持握姿态，由镐子资产提供默认值）。该姿态与主操作共用上半身动画层，因此主操作（如挖矿）优先。

### 1.3 钩索：用绳索摆荡回收抛出的镐子

抛出的镐子太重，无法被吸到视野前方。磁铁在这里的行为是**绳索约束**，而不是牵引力 —— 这是钩索手感的核心：

- **绳索是单侧距离约束**（`RopeConstraint`）。在绳长以内绳子是松弛的，玩家只受重力自由下落；到达绳长时**只抵消向外的径向速度分量，切向速度完整保留**，因此产生钟摆式摆荡。
- 用"朝锚点施加恒定加速度"实现（旧的做法）会持续消灭切向速度，永远只能得到"牵引光束"的手感，做不出摆荡。这是两种手感的根本分界。
- **绳索必须同时约束位置，而不只是速度**（`CalculatePositionCorrection`）。重力在约束之前就已被积分并应用，因此每帧会泄漏一个子帧的向外位移（`g * dt² ≈ 0.0056m/帧`，3 秒累积 0.166m 且持续增长）。只约束速度时绳子会被慢慢拉长，而速度修正随后过冲 —— 表现就是玩家在长度极限附近上下抖动。加入位置修正后实测误差与抖动都是 `0.00000m`。
- 绳子绷紧的**那一帧**施加一次性向内冲量（`ropeYankStrength` 默认 `0.35`，上限 `ropeMaximumYankSpeed` 默认 `7m/s`），读作"绳子被拽紧"。冲量按当时的向外速度缩放：缓慢飘移几乎无感，高速下坠则猛地一顿。
- 绳长以 `ropeReelInSpeed`（默认 `6m/s`）持续收短，把玩家拉向镐子；`ropeMinimumLength`（默认 `2.5m`）保证玩家不会被绞进锚点。绳长同样在每次 latch 时重置为当前距离。
- **收绳必须显式产生向内速度**：约束本身只会*移除*向外速度，永远不会*产生*向内运动。缺少这一步时绳长会缩短而玩家原地不动。向内速度是"设置"而非"累加"（`currentInward < reelSpeed` 才补足差值），累加会在几帧内冲到速度上限、变成朝锚点弹射。
- 绳子是否绷紧要和**实际距离**比较（含 `RopeTautTolerance` 容差），不能和收短后的绳长比较：后者会在收绳超过实际距离后永久报告"绷紧"，于是约束每帧和重力对抗。绳长也被 `Mathf.Min(ropeLength, distance)` 限制，否则玩家靠近后松弛会累积、再也绷不紧。
- **滚轮收放绳长**：每格 `1.5m`（`RopeReelMetresPerStep`），范围在 `ropeMinimumLength` 到 `pickaxeMagnetRange` 之间。可用于攀爬、缩短绳长加速、或放长绳越过边缘。绳索激活时滚轮不再调整普通磁铁的保持距离。
- **WASD 变成沿弧线的推力**（`ropeSwingAcceleration` 默认 `26`）。指向或背离锚点的输入无效（绳子无法沿自身长度推拉），只有横向分量驱动摆荡 —— 这就是"荡秋千"的助推。摆荡期间 WASD 不再驱动地面移动。
- **松手保留动量**（`ropeReleaseMomentum` 默认 `1.0` = 完整保留）。在弧线最低点松手会把玩家甩出去，这是摆荡的回报。旧实现在松手时清零速度，等于取消了这个回报。
- 重力**全程生效**。下坠是摆荡的能量来源，所以不能像旧的抓钩那样在附着时把重力归零。
- `ropeMaximumSpeed`（默认 `34m/s`）是绳索驱动速度的硬上限，避免长绳无限加速。

取得条件（只在**起手**时检查）：

- 取得范围为 `pickaxeMagnetRange`（默认 `25m`）。平瞄投掷的落点约 `13m`，所以这个范围覆盖正常投掷并留有余量。
- 必须**瞄准**镐子：偏离准星超过 `pickaxeMagnetAimAngle`（默认 `20°`）就取不到。此前的判定只要求镐子在前半球（等效 `90°` 锥角，比屏幕本身还宽），所以屏幕里任何一把镐子都会被吸，玩家无法选择目标。
- 必须**实际看得见**：准星到镐子之间不能有地形或其他碰撞体阻挡（`HasClearPickaxeSightline`）。视线检测瞄的是 `ThrownPickaxe.VisiblePosition`（沿木柄外露一段）而不是根节点——根节点位于质心、靠近已埋入地形的镐头，用它会被镐子自己钉入的地形判定为遮挡。
- latch 之后**不再**每帧检查瞄准和遮挡：只要不松开右键就一直保持，因此玩家可以自由看向别处、荡过拐角。

- **回收由玩家主动触发**：镐子已抛出时再按一次投掷键（默认 `G`）即召回，不限距离。镐子会拔出地面、进入 `Returning` 状态，一边旋转一边主动飞向玩家，飞到 `recallAbsorbDistance`（默认 `0.45m`）才被吸收。`recallTimeout` 保证飞行被卡住时也不会丢失镐子。
- 走近**不再**自动回收。此前靠近 `pickaxePickupDistance` 会自动触发，导致荡绳靠近时镐子被抽走、绳索中断。`pickaxePickupDistance` / `IsWithinPickupRange` 保留但仅作信息用途，不再驱动回收。
- 按 `G` 召回时若磁铁正牵引这把镐子，会先结束牵引，避免绳索继续拖拽一把正在飞回的镐子。同一次按住不会"召回后立刻又抛出"（按键需先松开再按）。
- 手持任意工具都能牵引镐子：磁铁的镐子调参统一从镐子的 `PlayerToolDefinition` 读取。

### 1.4 镐子投掷与召回（默认 G 键）

- 手上**没有**飞出的镐子时：按住 `G` 瞄准，显示抛物线预览线；松开掷出。
- 手上**已有**飞出的镐子时：按 `G` 直接召回（见 1.3），不进入瞄准。同一个键承担投掷和召回两种语义，按当前是否有镐子在外区分。
- 按键必须先松开再按下才能触发下一次动作，因此召回后仍按着 `G` 不会立刻又把镐子丢出去。
- 飞行中播放 `pickaxe_spin`（绕镐子质心旋转）；命中刚体后切到 `pickaxe_thrown`，镐头钉入被命中的 mesh，随后静止。
- 镐子会 parent 到被命中的对象，因此钉在会移动的物体上时会跟随移动，且永久存在直到被回收。
- 抛出后镐子从快捷栏消失，也无法在装备菜单中拖回；回收后自动放回原槽位。同一时刻只能有一把飞出的镐子。

`PlayerToolDefinition.animationTriggerMode` 是每个工具资产自己的表现配置。

## 2. 输入调用链

左键读取入口：

1. `PlayerToolController` 根据当前选中的 `CartTool` 设置 `cartTowEnabled`。
2. `FirstPersonCartAttractor.Update()` 以 `DefaultExecutionOrder(-300)` 优先读取左键按下，处理矿车把手的开始/解除牵引。
3. `VoxelPlayerController` 生成 `PlayerInputSnapshot`，处理工具动作、角色状态机、移动和动画。
4. 如果前一步开始或解除矿车牵引，`VoxelPlayerController` 会通过 `IsTowingCart` 和当帧点击消费标记抑制工具主操作，避免同一次左键同时进入其他工具动作。
5. 未牵引矿车且当前工具允许主操作时，`VoxelPlayerController` 进入统一的 `ToolAction` 状态。

右键和投掷键由 `VoxelPlayerController.TickSecondaryAction()` 处理。它在 locomotion 状态机之外运行，因此移动和挖矿期间磁铁与投掷都保持可用；两者互不排斥：

- 右键（恒为磁铁）→ `BeginAttraction(pickaxeDefinition)`、`TickAttraction()`、`EndAttraction()`。
- 投掷键 → `PickaxeThrowController.BeginAim()`、`ReleaseThrow()`、`CancelAim()`。

打开菜单（`GameHudController.IsGameplayInputBlocked`）时看不到右键松开事件，因此会调用 `CancelSecondaryAction()` 主动结束吸附并取消瞄准。

矿车牵引和磁铁使用各自独立的激活状态与结束方法。矿车取得失败不会清空正在运行的磁铁动作，结束磁铁动作也不会释放矿车牵引。

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

矿车位置力施加在 `CartHandle.AttachmentPoint`，目标速度取玩家 `CharacterController.velocity`。普通磁铁物体仍使用相机前方保持点，并额外受到最大吸附加速度限制。

## 4. 释放条件

矿车在以下情况下解除牵引：

- 牵引中再次按下左键；
- 离开第一人称；
- 矿车刚体变为 Kinematic；
- 把手相对目标位置超过 `breakDistance`；
- 玩家或吸附器组件被禁用。

牵引期间禁止切换快捷栏；禁用 Cart 工具或玩家组件会终止已经开始的矿车牵引。

## 5. 视觉表现

`MagnetAttractionBeam` 由 `FirstPersonCartAttractor.HasAttractionBeamTarget` 和 `AttractionBeamTarget` 驱动，两种吸附模式都会显示：持有普通刚体时终点为刚体重心，牵引已抛出的镐子时终点为镐子位置。光束是从双手中点到目标的单段稳定青绿色抛物线弧：几何不做横向摆动，只保留很轻的整体亮度呼吸和目标端高亮。`IsTowingCart` 为真时，光束会立即关闭。矿车车轮仍由 `PhysicalCartWheelAnimator` 根据刚体真实速度独立更新。

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
- `Assets/Game/Runtime/Gameplay/RopeConstraint.cs`
- `Assets/Game/Runtime/Gameplay/ThrownPickaxe.cs`
- `Assets/Game/Runtime/Gameplay/PickaxeThrowController.cs`
- `Assets/Game/Editor/Gameplay/ThrownPickaxeAssetBuilder.cs`
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
- `Assets/Game/Tests/Editor/RopeConstraintTests.cs`
- `Assets/Game/Tests/Editor/ThrownPickaxeTests.cs`
- `Assets/Game/Tests/Editor/WorldAndEffectTests.cs`
- `Assets/Game/Tests/Editor/CharacterCombatStateMachineTests.cs`
