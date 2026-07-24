# 第一人称角色与 Muryotaisu 动画

当前场景中的 `Player` 使用统一的 `Supernova.Voxels.VoxelPlayerController`。
它同时负责：

- WASD / 方向键相对视角移动；
- 鼠标第一人称视角；
- Space 跳跃和重力；
- F3 无碰撞调试飞行；
- Escape 切换鼠标锁定；
- Muryotaisu Animator 参数驱动。

## 动画参数

- `walkFlag`：落地且有移动输入；
- `jumpFlag`：起跳或处于空中；
- `idleFlag`：落地且静止；
- `idleBFlag`：持续静止 15 秒后触发备用待机；
- `smileFlag`：按住 Q；
- `kocchiFlag`：可选外部目标进入设定距离。第一人称相机是 Player 子节点时会被忽略，避免该状态永久开启。

动画仅作表现，`Animator.applyRootMotion` 被关闭；实际位移始终由 `CharacterController` 完成。

当前 `Player/CharacterVisual` 使用 `Assets/3rd/Mryotaisu/Animators/Muryotaisu.controller`，旧的 `MuryotaisuController` 不再挂载在当前场景角色上。

## 视角切换

按 `F5` 按以下顺序循环：

1. 第一人称：相机使用头部骨骼的动画位置和旋转；
2. 第二人称：右肩后方视角；
3. 第三人称：角色正后方远距离视角。

第二、第三人称通过 `PerspectiveCameraController` 每帧从角色头部向目标相机位置执行 `SphereCastNonAlloc`。除 Player 自己及其子物体外，所有 Layer 上的 Collider 都视为遮挡，包括 Trigger Collider。检测到遮挡时相机会立即向角色方向推进，遮挡消失后平滑恢复到配置距离。

Inspector 中可调整：

- `Second Person Offset`：第二人称肩部偏移；
- `Third Person Offset`：第三人称距离与高度；
- `Collision Radius`：相机碰撞球半径；
- `Collision Padding`：相机与遮挡面的安全距离；
- `Restore Smooth Time`：离开遮挡后恢复距离的平滑时间。

