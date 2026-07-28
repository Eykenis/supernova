# Minecraft 风格洞穴与无限区块生成

## 1. 范围与约定

本文描述 `Assets/Game` 中实现的四类 Minecraft 风格密度结构：Cheese、
Spaghetti、Noodle 和 Pillar，并说明它们如何组合成一个按 XZ 二维柱区块流送的
无限体素世界。

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

## 8. 32×32×256 柱区块数据布局

正式世界使用独立的 `VoxelColumnChunkData`：

- 水平尺寸为 `32 × 32`，完整高度为 `256`；
- 区块键为 `(chunkX, chunkZ)`，不存在垂直区块层；
- `OriginX = chunkX * 32`、`OriginY = 0`、`OriginZ = chunkZ * 32`；
- `InfiniteVoxelWorld.WorldToChunk` 对 X/Z 使用 floor division，负世界坐标同样正确。

旧 `VoxelVolume` / `VoxelChunkData` 的 `32³` 布局仍保留给 Gallery 和编辑器工具，
但不再承载正式世界。

生成器把 local `(x,y,z)` 转换为绝对坐标：

```text
world = chunkOrigin + local
```

再采样密度。Chunk 自身不参与随机数计算，所以区块边界不会改变噪声相位。

## 9. 无限世界流送

`MinecraftCaveInfiniteWorld` 固定使用 XZ 平面的欧氏半径 4：

```text
dx^2 + dz^2 <= 4^2
```

Required Set 一共有 49 根柱区块。初次出生只加载 `3×3` 的 9 根柱，碰撞网格完成并
释放玩家后再扩展到完整半径。当玩家跨越水平区块边界时，系统重新计算 Required Set：

1. 已存在的 Chunk 保留并直接复用。
2. 缺失的 Chunk 按到玩家的平方距离从近到远排队。
3. 密度数组在线程池中计算，不访问 GameObject、Mesh 或 World 字典。
4. 完成结果回到主线程后把数组所有权直接交给
   `InfiniteVoxelWorld`，不再逐体素复制 262,144 次。
5. 出生结构完成后，每根新柱提交时立刻进入网格队列，不等待半径内全部 49 根柱完成。
6. 一根数据柱拆成 8 个 `32×32×32` 网格分段，优先构建玩家所在高度附近的分段；
   每帧只构建一个分段。
7. 玩家离开加载半径的后台任务会收到取消信号，不再长期占用生成槽。
8. 离开半径的 Mesh 被卸载；已生成的密度 Chunk 保留，返回时无需重新计算。

这使世界坐标在理论上没有边界，而同时存在的渲染对象数量受半径 4 限制。

## 10. 柱区块网格边界

正式世界调用 `MarchingCubesMesher.BuildColumnSection`。一根数据柱仍覆盖完整
`32×256×32` 个 cell，但渲染上拆为 8 个 32 高分段。每个分段只缓存
`33×33×33` 个样本，因此 Marching Cubes、Mesh 创建和 MeshCollider cooking 的单次
主线程峰值约为原整柱工作的八分之一。相邻垂直分段共享边界采样，不遗漏跨分段 cell。

尚未生成的水平邻柱按普通实体处理，世界顶部之外按 Bedrock 处理，因此流送边缘和
垂直边界保持封闭。

当新柱从“默认实体”变为真实密度后，它只可能影响自身及 X/Z 负方向相邻网格：

```text
newColumn - (dx, 0, dz),  dx/dz in {0, 1}
```

流送器只把这些已生成且在视距内的 Mesh 标记为 dirty。

单个体素恰好位于 `y % 32 == 0` 时，同时重建上下两个分段；位于 X/Z 柱边界时仍会
重建负方向相邻柱的对应分段。玩家编辑使用独立高优先级队列，完成后会使普通队列中的
同坐标旧条目失效，避免重复重建。

## 11. Minecraft 式噪声格与本项目取舍

Minecraft 1.18 之后把 cave density 接入 noise router，并以分阶段 chunk generation
和圆柱形视距组织生成。本项目迁移其中适合连续等值面的两项思想：

- 昂贵密度函数只在全局对齐的粗格点求值，再在 cell 内插值；
- 数据生成、结构、网格、碰撞分阶段并渐进提交。

当前粗格为 X/Z 每 2 格、Y 每 4 格。单柱只执行 `17×65×17 = 18,785` 次完整
`Combined` 求值，再三线性展开为 262,144 个密度样本。相邻柱使用绝对世界坐标格点，
所以粗格相位连续。

在代表性的 `32×64×32` 密度体积上，与逐体素精确求值对比：

- 精确采样：约 `1025.7 ms`；
- XZ=2、Y=4 插值：约 `84.8 ms`；
- 加速约 `12.1×`，符号不一致约 `6.75%`，平均绝对误差约 `0.0096`。

这是项目当前噪声参数上的工程基准，不是跨硬件保证。它仍比 Minecraft 常见的更粗
噪声 cell 保守，以保留本项目较高频的 Noodle/Spaghetti 结构。没有切换到 16×16
数据柱：在相同世界半径下，单柱工作缩小四倍但柱数约增至四倍，不能解决完整密度函数
逐体素执行和整高网格主线程峰值这两个根因。

## 12. 固定高度与边界基岩

- 世界有效 Y 为 `0..255`；
- 出生点距顶部必须为 `32..160` 格，对应合法高度 `y=95..223`；
- `y=0` 和 `y=255` 强制写入 `Bedrock`（ID 4）；
- Bedrock 使用纯黑材质，耐久度为 `9999`；
- 矿物阶段不能替换 Bedrock，结构与出生点清理结束后还会再恢复两层边界。

## 13. 生命周期与隔离边界

- 所有新增源码、场景和文档都位于 `Assets/Game`。
- 只引用 `Supernova.Voxels` 的 public API，不编辑 `Assets/Game/Runtime/Voxels`。
- 运行时 Mesh 和 Material 使用 `HideFlags.DontSave`。
- 退出 Play Mode 或禁用组件时取消后台任务并销毁本组件创建的 Unity 对象。
- `MinecraftCaveGallery.scene` 用于比较五种密度结果。
- `MinecraftCaveInfiniteWorld.scene` 用于验证半径 4 的无限 Chunk 流送。

## 14. 相关文件

- `../Runtime/MinecraftCaveNoise.cs`
- `../Runtime/MinecraftCaveDensity.cs`
- `../Runtime/MinecraftCaveDensityInterpolator.cs`
- `../Runtime/MinecraftCaveVolumeGenerator.cs`
- `../Runtime/MinecraftCaveInfiniteWorld.cs`
- `../Runtime/MinecraftCaveFlyController.cs`
- `../Scenes/MinecraftCaveGallery.scene`
- `../Scenes/MinecraftCaveInfiniteWorld.scene`
