# 可投掷炸弹与通用区域效果

## 使用

在 `Assets/Scenes/InfiniteCaves.scene` 中，锁定鼠标后按 **G** 投掷炸弹。炸弹使用 `Rigidbody` 和连续碰撞检测，以初速度抛出，默认 2.5 秒后爆炸。

预制体：`Assets/Prefabs/TimedBomb.prefab`。

## 抽象层

炸弹并不直接引用体素系统：

1. `TimedBomb` 只创建 `AreaEffectContext`；
2. `AreaEffectDispatcher` 将区域效果广播给已注册的 `AreaEffectReceiverBehaviour`，并以 `OverlapSphereNonAlloc` 对附近刚体施加冲量；
3. `VoxelDestructionReceiver` 是 Minecraft 洞穴世界的适配器；
4. `DestructibleHealth` 展示了同一效果对普通生命值对象的处理。

因此，任何武器、技能、陷阱都能发送同一种区域效果；任何非体素对象也能通过实现 `IAreaEffectReceiver` 接收伤害或其他效果。

## 体素破坏性能路径

`MinecraftCaveInfiniteWorld.CarveSphere` 一次遍历爆炸包围盒：

- 内球无需计算随机哈希，外球之外立即跳过，仅在随机边界壳计算确定性哈希；
- 所有命中采样点一次性改为空气；
- 使用复用的 `HashSet<Vector3Int>` 去重受影响 chunk；
- 每个 dirty chunk 只入队一次，不逐体素重建；
- Mesh 与 MeshCollider 继续受 `meshesBuiltPerFrame` 帧预算控制；
- 只补充标记真正读取边界采样的负方向相邻 chunk。

随机性只改变爆炸边界，中心区域保证被移除，既保留可读的爆坑形状，也避免对每个体素进行昂贵噪声采样。

## 验证

`Assets/Game/Tests/Editor/BombAndVoxelEffectTests.cs` 覆盖：

- 区域效果可伤害非体素接收器；
- 同一 chunk 内大量体素修改只调度一次网格重建。
