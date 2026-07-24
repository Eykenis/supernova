# Minecraft 风格洞穴与无限区块生成

## 1. 范围与约定

本文描述 `Assets/Game` 中实现的四类 Minecraft 风格密度结构：Cheese、
Spaghetti、Noodle 和 Pillar，并说明它们如何组合成一个按三维 Chunk 流送的无限体素世界。

这套实现参考 Minecraft Java 版 1.18 之后的密度函数思想，但不是 Mojang 源代码的逐行复刻。
当前目标是复现四类空间的几何构造与组合语义；Aquifer、洞穴生物群系、传统路径 Carver、
矿脉和地表规则不在本阶段范围内。

整个系统使用统一符号：

```text
density >= 0  -> 实体
density < 0   -> 空气/洞穴
isoLevel = 0  -> Marching Cubes 提取的表面
```

该约定与现有 `Supernova.Voxels.VoxelVolume` 和 `MarchingCubesMesher` 一致。

## 2. 基础三维噪声

`MinecraftCaveNoise` 实现确定性的三维梯度 Perlin 噪声：

1. 将连续坐标分解为整数格点和格内小数坐标。
2. 使用 `seed + 格点坐标` 的整数哈希选择 12 个梯度方向之一。
3. 计算八个立方体角点的梯度点积。
4. 使用五次平滑曲线 `6t^5 - 15t^4 + 10t^3` 做三线性插值。
5. 多个 octave 按 lacunarity 提高频率、按 persistence 降低振幅后归一化。

NormalNoise 再把两组使用独立种子和坐标偏移的 octave Perlin 叠加：

```text
normal(p) = clamp(0.55 * (fractalA(p) + fractalB(1.0181269 * p + offset)), -1, 1)
```

所有噪声只依赖世界种子和绝对体素坐标，不读取 Unity 随机状态，也不依赖 Chunk 的生成顺序。
因此同一坐标无论由哪个 Chunk 请求，都会得到完全相同的浮点结果。

## 3. Cheese Cave

Cheese 负责尺度最大的洞厅和团块状空腔。它使用一组低频三维噪声作为主体，再叠加一组
Y 方向频率更高的分层噪声：

```text
cheese = normal4(p * cheeseFrequency)
layer  = normal2((p.x, 2.4 * p.y, p.z) * cheeseFrequency * 0.72)

Dcheese = cheese + cheeseThreshold + layer^2 * cheeseLayerStrength
```

`layer^2` 始终非负，会把部分位置推回实体，仅允许主体噪声足够低的位置形成空腔。
这比直接判断一个 Perlin 阈值更容易产生彼此分离但又能局部连通的大型洞厅。

参数作用：

- `cheeseFrequency` 越小，洞厅尺度越大。
- `cheeseThreshold` 越小，负密度区域越多，洞穴更空旷。
- `cheeseLayerStrength` 越大，洞穴越明显地被实体层分隔。

## 4. Spaghetti Cave

Spaghetti 的关键不是把单个噪声阈值化，而是求两个三维隐式曲面的交线：

```text
ridgeA = normal3(warpedPosition)
ridgeB = normal3(warpedPosition + independentOffset)

Dspaghetti = max(abs(ridgeA), abs(ridgeB)) - thickness + roughness
```

当 `Dspaghetti < 0` 时，必须同时满足：

```text
abs(ridgeA) < thickness
abs(ridgeB) < thickness
```

`ridgeA = 0` 和 `ridgeB = 0` 分别代表三维空间中的两张曲面。两张曲面的交集通常是一条曲线，
给这条曲线增加 thickness 后就得到连续的管状隧道。

在采样 ridge 前，系统还用三组低频噪声形成 domain warp：

```text
warpedPosition = p * frequency + warpVector * spaghettiWarp
```

它使隧道产生大尺度转弯。独立的 thickness noise 改变沿线宽度，roughness noise 只扰动边界，
避免管壁过分规则。

## 5. Noodle Cave

Noodle 使用与 Spaghetti 相同的双曲面交线原理，但频率更高、厚度更小，并增加稀有度门控：

```text
activation = normal2(p * noodleFrequency * 0.31)

if activation < noodleRarity:
    Dnoodle = 1                    // 正密度，本区域禁用
else:
    Dnoodle = max(abs(a), abs(b)) - noodleThickness
```

因此 Noodle 不会均匀铺满世界，而只在 activation 允许的连续区域中出现。与 Spaghetti 相比，
它产生更细、更密、更容易形成支路和局部捷径的通道。

参数作用：

- `noodleFrequency` 控制曲线弯折密度。
- `noodleThickness` 控制管径。
- `noodleRarity` 越高，启用区域越少。

## 6. Pillar

Pillar 与前三类不同：它不是负密度空洞，而是要填回洞穴的正密度实体。

采样坐标采用强各向异性缩放：

```text
pillarPoint = (
    p.x * pillarHorizontalFrequency,
    p.y * pillarVerticalFrequency,
    p.z * pillarHorizontalFrequency)
```

垂直频率远低于水平频率，所以噪声沿 Y 变化缓慢、沿 XZ 变化较快，形成纵向延伸的柱体。

```text
gate = pillarNoise - pillarRarity - 0.16 * rarenessNoise
thicknessScale = clamp01(0.62 + 0.38 * thicknessNoise)^3

Dpillar = gate * thicknessScale * pillarStrength
```

`gate > 0` 的稀有区域成为实体柱，三次方 thicknessScale 会收紧边界并改变柱径。
单独展示 Pillar 时，代码先创建椭球洞厅，再用 `max(chamber, pillar)` 把石柱填回洞厅。

## 7. 四类结构的组合与空间章法

直接执行 `min(Cheese, Spaghetti, Noodle)` 会把三个全局噪声场的空洞做并集。只要每个输入
分别拥有较高的负密度比例，最终结果就会发生 percolation：几乎所有洞穴连成一张空旷、
无边界的网络。因此当前实现先用三个更低频的 layout noise 划分宏观洞穴区。

房间必须同时通过两个独立门控：

```text
primaryGate = layoutThreshold - layoutA
roomGate    = max(primaryGate, layoutThreshold - layoutB)
rooms       = max(Cheese, roomGate)
```

`max` 表示 Cheese 和两个 gate 必须同时为负。两个低频区域的交集形成彼此分离的大体积，
外部保持正密度厚岩墙，因此 Cheese 不再能够在整个世界自由贯通。

主走廊只允许出现在 primary 区域更深的位置：

```text
corridors = max(Spaghetti, primaryGate + corridorInset)
```

`corridorInset` 把走廊从区域边缘向内收缩。它可以连接同一宏观房间群中的相邻洞厅，
但不能跨越整个正密度分区。

Noodle 还必须通过第三个独立 gate：

```text
shortcutGate = max(
    primaryGate + shortcutInset,
    layoutThreshold + corridorInset - layoutC)
shortcuts = max(Noodle, shortcutGate)
```

它因此只作为少量捷径出现，而不是在每个房间墙面制造碎孔。最后才合并三类空洞并回填柱体：

```text
voids    = min(rooms, corridors, shortcuts)
combined = max(voids, Pillar)
```

默认种子在 `128^3` 世界范围、每 2 格粗采样一次的拓扑验证中，空体积约为 10%，最大连通
空间小于全部空体积的 50%。这代表地下仍有可探索的房间群，但不再由一个网络占据绝大多数空间。

## 8. 32^3 Chunk 数据布局

本实现直接引用 `Scripts/Voxel` 的既有类型，不修改它们：

- `VoxelVolume.Size == 32`
- `VoxelVolume.VoxelCount == 32 * 32 * 32`
- `VoxelChunkData.OriginX/Y/Z == ChunkCoordinate * 32`
- `InfiniteVoxelWorld.WorldToChunk` 使用 floor division，负世界坐标同样正确。

生产入口为：

```csharp
MinecraftCaveVolumeGenerator.FillChunk(chunk.Data, densityField, MinecraftCaveType.Combined);
```

它把 local `(x,y,z)` 转换为绝对坐标：

```text
world = chunkOrigin + local
```

再采样密度。Chunk 自身不参与随机数计算，所以区块边界不会改变噪声相位。

## 9. 无限世界流送

`MinecraftCaveInfiniteWorld` 固定使用三维欧氏半径 4：

```text
dx^2 + dy^2 + dz^2 <= 4^2
```

边长为 9 的候选立方体经过球形筛选后，玩家附近的 Required Set 一共有 257 个 Chunk。
当玩家跨越 Chunk 边界时，系统重新计算 Required Set：

1. 已存在的 Chunk 保留并直接复用。
2. 缺失的 Chunk 按到玩家的平方距离从近到远排队。
3. 密度数组在线程池中计算，不访问 GameObject、Mesh 或 World 字典。
4. 完成结果回到主线程后写入 `InfiniteVoxelChunk.Data`。
5. 主线程按每帧预算调用现有 `MarchingCubesMesher.BuildChunk`。
6. 离开半径的 Mesh 被卸载；已生成的密度 Chunk 保留，返回时无需重新计算。

这使世界坐标在理论上没有边界，而同时存在的渲染对象数量受半径 4 限制。

## 10. Chunk 网格边界

现有 `MarchingCubesMesher.BuildChunk` 对一个 32 样本 Chunk 构建 32 个 cell，并额外请求
`+X/+Y/+Z` 的邻接样本。尚未生成的邻区块按实体处理，因此流送边缘会暂时封闭，不会出现
只在正方向产生的假开口。

当新 Chunk 从“默认实体”变为真实密度后，它可能影响自身，以及坐标分别减去 0 或 1 的
八个 Chunk 网格：

```text
newChunk - (dx, dy, dz),  dx/dy/dz in {0, 1}
```

流送器只把这些已生成且在视距内的 Mesh 标记为 dirty，避免每生成一个 Chunk 就重建完整
`3 * 3 * 3` 邻域。

## 11. 生命周期与隔离边界

- 所有新增源码、场景和文档都位于 `Assets/Game`。
- 只引用 `Supernova.Voxels` 的 public API，不编辑 `Assets/Game/Runtime/Voxels`。
- 运行时 Mesh 和 Material 使用 `HideFlags.DontSave`。
- 退出 Play Mode 或禁用组件时取消后台任务并销毁本组件创建的 Unity 对象。
- `MinecraftCaveGallery.scene` 用于比较五种密度结果。
- `MinecraftCaveInfiniteWorld.scene` 用于验证半径 4 的无限 Chunk 流送。

## 12. 相关文件

- `../Runtime/MinecraftCaveNoise.cs`
- `../Runtime/MinecraftCaveDensity.cs`
- `../Runtime/MinecraftCaveVolumeGenerator.cs`
- `../Runtime/MinecraftCaveInfiniteWorld.cs`
- `../Runtime/MinecraftCaveFlyController.cs`
- `../Scenes/MinecraftCaveGallery.scene`
- `../Scenes/MinecraftCaveInfiniteWorld.scene`
