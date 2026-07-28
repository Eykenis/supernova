# 矿车物理、第一人称吸附器与玩家工具系统工作总结

## 1. 工作目标

本次对话围绕以下功能进行了实现和调整：

1. 为 `EmptyCart` 矿车模型添加完整的动态物理配置。
2. 矿车能够被玩家或其他物理对象推动并滑动。
3. 矿车车轮根据实际刚体运动播放对应的滚动效果。
4. 为第一人称玩家添加软物理吸附器，可拖拽矿车且不破坏矿车碰撞。
5. 修正吸附器最初只在水平面作用、无法改变矿车 Y 轴位置的问题。
6. 添加玩家工具模式：
   - 数字键 `1`：镐子，左键维持原有挖掘功能。
   - 数字键 `2`：磁铁，按住左键吸附准星下的动态刚体，松开左键释放。

---

## 2. 矿车物理系统

### 2.1 修改的预制体

- `Assets/3rd/EmptyCart.prefab`

### 2.2 刚体配置

矿车根节点添加并配置了动态 `Rigidbody`：

- Mass：`65`
- Drag：`0.08`
- Angular Drag：`2.5`
- Use Gravity：启用
- Is Kinematic：关闭
- Interpolation：`Interpolate`
- Collision Detection：`Continuous Dynamic`
- 冻结 X、Z 旋转，避免矿车在一般推动过程中轻易侧翻。
- Y 轴旋转保持开放，使矿车能够通过碰撞或吸附扭矩转向。
- 重心被适当降低，以提高稳定性。

矿车的位移没有使用 Transform 驱动，仍然完全由刚体、碰撞和外部物理力产生。

### 2.3 碰撞体

矿车使用复合碰撞结构：

- 车体和把手使用多个 `BoxCollider`。
- 四个轮子使用模型原始网格生成的凸面 `MeshCollider`。
- 当前矿车预制体总计包含 `7` 个碰撞体。

矿车保持为普通动态刚体，因此可以：

- 被玩家推动。
- 与地形、墙壁和其他刚体碰撞。
- 在碰撞阻挡下停止或改变运动。
- 在吸附时继续参与碰撞，而不是穿过障碍物强制跟随玩家。

### 2.4 低摩擦材质

新增物理材质：

- `Assets/3rd/CartPhysics/CartLowFriction.physicMaterial`

主要配置：

- Dynamic Friction：`0.18`
- Static Friction：`0.22`
- Bounciness：`0`
- Friction Combine：`Minimum`
- Bounce Combine：`Minimum`

该材质用于保证矿车可以被推动并产生较自然的滑动，同时不会明显弹跳。

---

## 3. 车轮滚动表现

### 3.1 新增脚本

- `Assets/Game/Runtime/Physics/PhysicalCartWheelAnimator.cs`

### 3.2 工作方式

原模型的轮子网格顶点使用了偏移坐标，直接旋转原节点会绕错误的中心旋转。因此为四个车轮分别创建了独立旋转中心：

- `CartWheelVisuals/Group47111_Pivot`
- `CartWheelVisuals/Group47126_Pivot`
- `CartWheelVisuals/Group47141_Pivot`
- `CartWheelVisuals/Group47156_Pivot`

原始轮子 Renderer 被关闭，轮子网格被复制到对应的旋转中心下显示。

滚动脚本使用：

```csharp
Rigidbody.GetPointVelocity(wheelPivot.position)
```

读取每个车轮所在位置的实际刚体速度，再按照轮子半径换算旋转角度。因此：

- 前进和后退时轮子会反向滚动。
- 转弯时各车轮会根据所在位置的速度产生滚动。
- 脚本只更新轮子视觉，不向矿车施加位移或驱动力。
- 矿车的真实运动仍全部来自物理系统。

---

## 4. 可吸附物体标记

### 4.1 新增脚本

- `Assets/Game/Runtime/Physics/PhysicsAttractable.cs`

### 4.2 用途

该组件仍保留在 `EmptyCart` 根节点，供旧内容记录“可吸附”元数据。磁铁现在直接
接受准星命中的任意动态 `Rigidbody`，不再要求目标必须带此标记。

主要接口：

```csharp
bool CanBeAttracted
Rigidbody Body
void SetCanBeAttracted(bool value)
```

磁铁目标只需满足：

- 准星射线首先命中其碰撞体；
- 碰撞体关联到 `Rigidbody`；
- Rigidbody 不是 Kinematic；
- 目标不属于玩家自身层级。

---

## 5. 第一人称刚体吸附器

### 5.1 新增脚本

- `Assets/Game/Runtime/Gameplay/FirstPersonCartAttractor.cs`

该组件被添加到：

- `Assets/Game/Prefabs/Player.prefab`

### 5.2 目标取得

吸附器仅在以下条件下工作：

- 装置已启用。
- 玩家处于第一人称模式。
- 鼠标光标处于锁定状态。
- 玩家按住鼠标左键。

目标检测使用从相机准星发出的非分配式射线：

```csharp
Physics.RaycastNonAlloc(...)
```

默认参数：

- 最大取得距离：`3.5m`
- 检测层：`targetLayers`

如果玩家与目标之间存在更近的静态碰撞体，则不会隔着障碍物取得刚体。矿车与
采出的矿石刚体走同一条取得和软物理吸附链路。

### 5.3 固定力物理吸附

吸附器不会执行以下操作：

- 不修改矿车 Transform 位置。
- 不把矿车设为 Kinematic。
- 不把矿车设置为玩家子节点。
- 不使用瞬移方式强制跟随。

磁铁向目标点施加有上限的真实物理力：

```csharp
force = normalize(positionError) * attractionForce
    + relativeVelocity * forceDamping;
force = ClampMagnitude(force, attractionForce);

rigidbody.AddForce(force, ForceMode.Force);
```

默认参数：

- Attraction Force：`800N`
- Force Damping：`90N/(m/s)`
- Hold Distance：`2m`
- Hold Distance 范围：`0.5m`–`6m`
- 每格滚轮距离：`0.35m`
- Break Distance：`8m`

`ForceMode.Force` 不会绕过质量：同样的 800N 对轻物体产生较大加速度，对重物体
产生较小加速度。如果向上的力不足以抵消 `mass * gravity`，物体就无法离地。磁铁
不会读取质量后用代码判断“允许/禁止抬起”，取得逻辑仍只检查是否为动态刚体。

吸附期间滚轮向上会把目标点推远，滚轮向下会把目标点拉近。目标点始终位于准星
方向，并限制在配置的最小、最大距离内。

矿车朝向通过 `ForceMode.Force` 的 Y 轴扭矩逐渐对齐玩家前方：

```csharp
rigidbody.AddTorque(yawTorque, ForceMode.Force);
```

### 5.4 Y 轴吸附修正

最初的吸附实现主动将目标点 Y 坐标设为矿车当前重心高度，并将速度投影到水平面，因此矿车无法发生垂直运动。

现已修改为完整三维吸附目标：

```csharp
Vector3 desiredPosition = viewCamera.transform.position
    + viewCamera.transform.forward.normalized * holdDistance;
```

同时取消了速度的水平面投影：

```csharp
Vector3 targetVelocity = playerController.velocity;
Vector3 bodyVelocity = heldBody.velocity;
```

现在矿车会沿 X、Y、Z 三个轴受到软弹簧力：

- 平视时会被吸到相机正前方。
- 向上或向下看时，矿车目标高度会对应改变。
- 重力仍然生效。
- 矿车仍会和天花板、地面、墙壁等发生正常碰撞。

运行验证中，矿车 Y 坐标从 `0.05` 上升到约 `1.28`，垂直位移约 `1.23m`，同时仍保持动态刚体及全部 7 个碰撞体。

### 5.5 释放条件

以下任意情况会释放矿车：

- 松开鼠标左键。
- 吸附器工具被关闭。
- 切换到第三人称。
- 矿车超过最大断开距离。
- 目标被设为不可吸附。
- 玩家组件或吸附器组件被禁用。

---

## 6. 玩家工具切换系统

### 6.1 新增脚本

- `Assets/Game/Runtime/Gameplay/PlayerToolController.cs`

新增工具枚举：

```csharp
public enum PlayerToolMode
{
    Pickaxe = 1,
    CartAttractor = 2,
}
```

该组件已添加到：

- `Assets/Game/Prefabs/Player.prefab`

### 6.2 操作

同时支持主键盘数字键和数字小键盘：

| 按键 | 工具 | 左键功能 |
|---|---|---|
| `1` / `Keypad 1` | Pickaxe | 保留原有攻击/挖掘流程，执行 mining |
| `2` / `Keypad 2` | Magnet | 按住吸附准星下的动态刚体，松开释放 |

默认工具为：

```text
Pickaxe
```

### 6.3 输入互斥

修改文件：

- `Assets/Game/Runtime/Voxels/VoxelPlayerController.cs`

玩家控制器在读取左键攻击时会检查吸附器是否正在占用主操作：

```csharp
cartAttractor == null || !cartAttractor.ConsumesPrimaryAction
```

因此：

- 选择镐子时，吸附器关闭，左键继续进入原来的 `Attack`/`Mine` 状态流程。
- 选择吸附器时，吸附器启用，左键不会同时触发挖掘动画和 mining。
- 从吸附器切换回镐子时，会立即调用吸附器释放逻辑。

### 6.4 代码控制接口

工具可以通过代码切换：

```csharp
playerToolController.SelectTool(PlayerToolMode.Pickaxe);
playerToolController.SelectTool(PlayerToolMode.CartAttractor);
```

也可以独立控制吸附器：

```csharp
cartAttractor.SetDeviceEnabled(true);
cartAttractor.SetDeviceEnabled(false);
```

通常应优先通过 `PlayerToolController` 切换，避免工具状态不一致。

---

## 7. 本次新增和修改的文件

### 新增

- `Assets/Game/Runtime/Physics/PhysicalCartWheelAnimator.cs`
- `Assets/Game/Runtime/Physics/PhysicsAttractable.cs`
- `Assets/Game/Runtime/Gameplay/FirstPersonCartAttractor.cs`
- `Assets/Game/Runtime/Gameplay/PlayerToolController.cs`
- `Assets/3rd/CartPhysics/CartLowFriction.physicMaterial`

### 修改

- `Assets/3rd/EmptyCart.prefab`
- `Assets/Game/Prefabs/Player.prefab`
- `Assets/Game/Runtime/Voxels/VoxelPlayerController.cs`

---

## 8. 验证记录

已完成的针对性验证包括：

- 矿车存在动态 Rigidbody，且不是 Kinematic。
- 矿车包含 7 个复合碰撞体。
- 矿车在物理冲量下能够滑动。
- 车轮会根据实际刚体运动旋转。
- 吸附目标能够通过球形投射取得。
- 关闭吸附器会立即释放目标。
- 吸附过程中矿车没有被设置为玩家子节点。
- 吸附过程中矿车保持动态刚体。
- 吸附力能够同时改变 X、Y、Z 位置。
- 工具 `1` 默认选择镐子，并关闭吸附器。
- 工具 `2` 会启用吸附器并占用左键主操作。

在较早阶段，项目原有 EditMode 测试曾达到 `26/26` 全部通过。

在生成本文档前的最新一次完整 EditMode 测试中，共发现 `34` 个测试，其中 `32` 个通过、`2` 个失败：

1. `BombAndVoxelEffectTests.ViewerMovement_RefreshesStreamingWhileMeshesAreStillQueued`
   - 期望 `(1, 0, 0)`，实际 `(0, 0, 0)`。
2. `FirstPersonAnimationControllerTests.UnifiedController_DrivesMuryotaisuAnimatorContract`
   - `TargetParameterCountException`。

这两个失败未直接指向本次新增的矿车、吸附器或工具切换脚本，但在宣称项目完整测试通过前仍应单独调查。

---

## 9. 可调参数建议

如果吸附感觉太硬：

- 降低 `positionSpring`。
- 降低 `maximumAcceleration`。
- 适当提高 `positionDamping`，减少振荡。

如果矿车跟随过慢：

- 提高 `positionSpring`。
- 提高 `maximumAcceleration`。

如果矿车上下跳动明显：

- 提高 `positionDamping`。
- 适当降低 `positionSpring`。
- 检查目标点是否因相机动画产生快速上下抖动。

如果矿车转向过快：

- 降低 `yawSpring` 或 `maximumAngularAcceleration`。

如果希望吸附位置低于准星，可在 `FirstPersonCartAttractor` 中增加相机局部空间偏移，例如：

```csharp
Vector3 desiredPosition = viewCamera.transform.position
    + viewCamera.transform.forward * holdDistance
    + viewCamera.transform.up * verticalOffset;
```

其中 `verticalOffset` 可以使用负值，让矿车保持在准星下方。
