# MinecraftCaves 世界生成与 Voxel 依赖

本文只总结 `Assets/Game` 中与洞穴世界生成直接相关的运行时代码，以及这条生成链路实际引用的 `Assets/Game/Runtime/Voxels` 内容。

不包含生物、寻路、玩家控制、相机控制、场景灯光配置，也不包含 `Voxel` 目录中的房间模板、SDF 雕刻和通用洞穴生成系统。

## 1. 生成链路概览

正式无限世界的入口是 `MinecraftCaveInfiniteWorld`，数据流如下：

```text
世界种子 + 绝对体素坐标
        ↓
MinecraftCaveNoise：确定性三维梯度噪声
        ↓
MinecraftCaveDensityField：Cheese / Spaghetti / Noodle / Pillar / Combined 密度
        ↓
全局对齐的 2×4×2 噪声格求值 + 三线性展开
        ↓
后台任务生成单根柱的 float[32×256×32]
        ↓
数组所有权直接提交到 InfiniteVoxelWorld / VoxelColumnChunkData
        ↓
MarchingCubesMesher.BuildColumnSection（每柱 8 个 32 高分段）
        ↓
GameObject + MeshFilter + MeshRenderer + 可选 MeshCollider
```

系统统一使用以下密度符号：

```text
density >= 0  -> 实体
density < 0   -> 洞穴/空气
isoLevel = 0  -> 网格表面
```

MinecraftCaves 自己生成完整密度场，不调用 `Voxel` 中的 SDF 挖空流程。

## 2. 确定性三维噪声

`MinecraftCaveNoise` 实现基于绝对坐标的三维梯度 Perlin 噪声。

### 2.1 单层 Perlin

对每个采样位置：

1. 使用 `floor` 找到所在整数晶格；
2. 对立方体 8 个角点执行包含 X/Y/Z 和 seed 的整数哈希；
3. 从 12 个固定三维梯度方向中选取梯度；
4. 计算梯度与角点到采样点偏移的点积；
5. 使用 `6t^5 - 15t^4 + 10t^3` 平滑曲线；
6. 沿 X、Y、Z 三个方向插值得到结果。

哈希最后经过 avalanche 混合，因此不需要保存置换表，也不读取 `UnityEngine.Random`。

### 2.2 分形与 Normal Noise

`FractalPerlin` 叠加多个 octave：

- 每层频率乘 `lacunarity`，默认 `2`；
- 每层振幅乘 `persistence`，默认 `0.5`；
- 最终除以振幅总和，使不同 octave 数量仍保持相近范围。

`NormalNoise` 再组合两组独立的分形噪声：

```text
A = fractal(position, seed)
B = fractal(position * 1.0181269 + seedOffset, secondSeed)
result = clamp((A + B) * 0.55, -1, 1)
```

噪声只取决于 seed 和绝对世界坐标。因此 chunk 的生成顺序、线程调度和局部坐标都不会改变同一世界采样点的结果。

## 3. 洞穴密度场

`MinecraftCaveDensityField` 提供五种密度类型。其中前四种是基础形态，`Combined` 是无限世界实际使用的组合结果。

### 3.1 Cheese

Cheese 使用低频主体噪声生成大体积洞厅，再加入 Y 方向频率更高的分层项：

```text
cheese = normal4(position * cheeseFrequency)
layer  = normal2((x, y * 2.4, z) * cheeseFrequency * 0.72)

Dcheese = cheese
          + cheeseThreshold
          + layer² * cheeseLayerStrength
```

`layer²` 始终非负，会把部分区域推回实体，从而把大空腔分隔成具有层次的洞厅。

### 3.2 Spaghetti

Spaghetti 先用三组低频噪声对采样域进行三维扭曲，然后求两个独立隐式噪声面的交线：

```text
warped = position * spaghettiFrequency + warp * spaghettiWarp
Dspaghetti = max(abs(ridgeA), abs(ridgeB))
             - thickness
             + roughness
```

只有 `ridgeA` 和 `ridgeB` 同时接近零时密度才为负，因此两张隐式面的交线形成带厚度的管状隧道。独立噪声会轻微改变厚度和表面粗糙度。

### 3.3 Noodle

Noodle 使用更高频、更细的双隐式面交线，并先执行稀有度门控：

```text
activation < noodleRarity -> 返回 1，当前区域禁用
否则：
Dnoodle = max(abs(ridgeA), abs(ridgeB)) - thickness
```

因此 Noodle 只在连续的许可区域出现，用于形成少量细通道，而不是遍布整个世界。

### 3.4 Pillar

Pillar 是用于填回洞穴的实体密度。其输入对 X/Z 使用较高频率，对 Y 使用很低频率：

```text
pillarPoint = (
    x * horizontalFrequency,
    y * verticalFrequency,
    z * horizontalFrequency)
```

噪声沿 Y 变化较慢，从而形成纵向延伸的石柱。稀有度噪声控制出现区域，厚度噪声经过三次方收紧后控制柱径。

### 3.5 Combined

直接合并所有负密度场容易产生贯穿世界的空洞网络。`Combined` 先用三组更低频的 layout noise 划分宏观允许区域。

洞厅必须同时通过两个布局门：

```text
primaryGate = layoutThreshold - layoutA
roomGate    = max(primaryGate, layoutThreshold - layoutB)
rooms       = max(Cheese, roomGate)
```

Spaghetti 只允许出现在主区域向内收缩后的范围：

```text
corridors = max(Spaghetti, primaryGate + corridorInset)
```

Noodle 还必须通过第三组独立布局噪声，只形成偶发捷径：

```text
shortcutGate = max(
    primaryGate + shortcutInset,
    layoutThreshold + corridorInset - layoutC)
shortcuts = max(Noodle, shortcutGate)
```

最后合并空腔，并用 Pillar 的正密度恢复实体：

```text
voids    = min(rooms, corridors, shortcuts)
combined = max(voids, Pillar)
```

在“负值为空”的约定下，`min` 相当于空腔并集，`max` 相当于施加共同限制或把实体填回。

## 4. 从密度场写入体素

`MinecraftCaveVolumeGenerator` 是密度场到 Voxel 容器的适配层。

### 4.1 世界柱区块

Gallery/工具中的 `FillColumn` 仍可逐体素精确采样。正式无限世界改由
`MinecraftCaveDensityInterpolator` 从 `VoxelColumnChunkData.OriginX/Z` 取得绝对
原点，在 X/Z 间隔 2、Y 间隔 4 的全局格点求值完整 `Combined`，再三线性展开：

```text
coarse samples = 17 × 65 × 17 = 18,785
expanded samples = 32 × 256 × 32 = 262,144
```

绝对坐标采样保证相邻柱不会因为各自局部坐标归零而产生噪声接缝，也保证负区块坐标
得到同一连续密度场。

无限世界的后台生成函数执行相同逻辑，同时写入独立的
`float[262144]` 和 `VoxelTypeId[262144]`。普通实心样本先分配为 Stone，顶部
`y=255` 与底部 `y=0` 写为 Bedrock，再由普通矿团阶段替换允许替换的类型；工作线程
不会访问世界字典和 Unity 资产。

### 4.2 展示体积与正式世界的区别

`FillDisplayVolume` 只服务于 `MinecraftCaveGallery`：它把一个 `32³` 体积居中到原点，并额外与盒形容器和可选剖切平面组合，以便独立观察各密度类型。

正式无限世界使用 `SampleFeatureDensity(..., Combined)`，没有展示盒和剖切面。

## 5. 二维柱区块流送

`MinecraftCaveInfiniteWorld` 固定维护玩家周围 XZ 欧氏半径为 4 的二维区块圆盘：

```text
dx² + dz² <= 4²
```

满足条件的偏移共 49 个，并在初始化时按距离平方由近到远排序。Y 不参与区块寻址；
每个条目都覆盖完整 `0..255` 高度。初次出生阶段先加载 `3×3` 的 9 根柱，玩家释放后
扩展到完整半径。

玩家跨越 chunk 边界时，系统重新建立 required set：

- 已生成、仍在范围内的 chunk 直接复用；
- 缺失 chunk 按近到远进入密度生成队列；
- 已生成但没有当前网格的 chunk 进入网格队列；
- 范围外的 GameObject 和 Mesh 被卸载；
- `InfiniteVoxelWorld` 中已经提交的密度数据不删除，玩家返回时无需重新采样。

世界坐标到 chunk 坐标使用 `InfiniteVoxelWorld.WorldToChunk` 的 floor division，所以负坐标能够正确映射。

## 6. 后台密度/类型生成与主线程提交

密度采样通过 `Task.Run` 在线程池执行，并由 `maxConcurrentGenerationJobs` 限制同时运行的任务数。

每个任务：

1. 分配一个 `float[VoxelColumnChunkData.VoxelCount]` 和一个
   `VoxelTypeId[VoxelColumnChunkData.VoxelCount]`；
2. 根据 XZ 柱坐标计算绝对原点，Y 原点恒为 `0`；
3. 在全局对齐的 `17×65×17` 格点计算 `Combined`，三线性展开完整密度数组；
4. 为普通实心样本分配 Stone，并把 `y=0/255` 写为 Bedrock；随后用确定性
   `MinecraftOreFeatureGenerator` 写入普通矿团类型，Bedrock 不属于可替换类型；
5. 每完成一个 Z 切片以及每次矿团尝试时检查取消令牌；
6. 返回坐标、密度和类型数组，不访问 `GameObject`、`Mesh`、世界字典或
   `ScriptableObject`。

主线程在 `Update` 中轮询已完成任务。只有结果对应的 chunk 仍在 required set 且尚未生成时才提交：

1. 调用 `InfiniteVoxelWorld.AddChunkTakingOwnership`（内部键为 `Vector2Int`）；
2. 直接把任务生成的密度与类型数组交给 `VoxelColumnChunkData`，不再逐项复制；
3. 标记受该新密度影响的网格；结构阶段完成后，新柱无需等待其余 required set，
   会立即进入渐进网格队列。

每个生成任务有独立的链接取消令牌。玩家跨柱后，离开 required set 的在途任务会被
取消；若坐标很快重新进入 required set，则旧任务清理后重新排队。组件禁用、销毁或
应用退出时仍会取消整个生成令牌并清理运行时状态。

## 7. Chunk 边界与网格更新

MinecraftCaves 调用 `MarchingCubesMesher.BuildColumnSection`。一根数据柱保存
`32×256×32` 个采样点，但分成 8 个 32 高网格段；每段处理 `32³` 个 cell，并预取
`33³` 个样本。分段共享 Y 边界采样，合计覆盖的 cell 与原整柱完全一致。

未生成的水平邻柱按 Stone 与实体密度 `isoLevel + 1` 处理；世界高度之外按 Bedrock
处理。这样加载边界与顶部/底部都会封闭。

新柱从“未知且按实体处理”变为真实密度后，可能改变它自己以及 X/Z 负方向的相邻网格：

```text
affected = generatedColumn - (dx, 0, dz)
dx, dz ∈ {0, 1}
```

系统只把 required set 内且已有体素数据的这些分段加入 dirty set。队列优先从玩家
所在高度向上下展开；`dirtyMeshes` 去重，`meshesBuiltPerFrame` 限制主线程每帧最多
重建多少个分段。高优先级编辑重建完成后，普通队列中的旧条目会被跳过。

重建时先销毁该坐标的旧运行时对象，然后：

1. 运行 Marching Cubes；
2. 空网格只记录为已构建，不创建 GameObject；
3. 非空网格创建 Mesh、MeshFilter 和 MeshRenderer；
4. 根据设置选择是否创建 MeshCollider；
5. 柱对象放到 `(coordinate.x * 32, 0, coordinate.z * 32) * voxelSize`
   的局部位置。

## 8. 出生点选择

启用 `placeViewerInCave` 时，系统用世界种子派生一个确定性 `System.Random`：

- 第一次检查合法高度带中央的 `(0,159,0)`；
- 随后最多在 X/Z `[-72,72]`、Y `95..223` 中尝试确定性随机点，总尝试数为 2400；
- 候选点密度必须小于 `-0.035`；
- 候选中心及六个轴向、距离为 2 的采样点必须为空。

找到合格点立即作为出生体素；若没有找到，则使用所有候选中密度最低的位置。

## 9. 实际引用的 Voxel 内容

MinecraftCaves 世界生成只使用以下 `Supernova.Voxels` 能力。

### 9.1 `VoxelColumnChunkData`

- 固定尺寸 `32 × 256 × 32`；
- 连续 `float[262144]` 密度和 `VoxelTypeId[262144]` 类型存储；
- 提供局部 `(x,y,z)` 索引；
- X/Z 决定柱坐标，Y 是柱内的绝对世界高度。

旧 `VoxelVolume` / `VoxelChunkData` 仍服务 Gallery 和结构编辑器，不属于正式世界
的流送数据。

### 9.2 `InfiniteVoxelChunk` 与 `InfiniteVoxelWorld`

- `InfiniteVoxelWorld` 用 `Dictionary<Vector2Int, InfiniteVoxelChunk>` 缓存已提交的柱；
- `EnsureChunk` 创建或返回指定柱；
- `TryGetChunk` 用于流送状态和网格构建判断；
- `GetDensityOrDefault` 为跨柱网格采样提供世界密度；
- `WorldToChunk` 使用 floor division 处理负 X/Z，兼容返回值的 Y 恒为 0；
- 世界 Y 仅允许 `0..255`。

`InfiniteVoxelChunk` 创建时默认填充正密度 `1`，但 MinecraftCaves 在提交后台
结果时会用生成的完整密度和类型数组覆盖它。

### 9.3 `MarchingCubesMesher`

- 使用固定 `256 × 16` case 表；
- 每个 cell 读取 8 个角点并形成 8-bit case；
- case `0` 和 `255` 跳过；
- 正式场景使用密度边插值定位零交点；
- `BuildChunk` 预取并复用 `33×257×33` 样本缓存；
- 当前静态密度缓存不支持多个线程并发调用，因此网格构建保留在主线程串行执行。

### 9.4 `VoxelMeshData`

- 保存顶点和三角形索引；
- 创建 Unity Mesh 时自动选择 16/32 位索引；
- 上传三角形后重新计算法线和 Bounds。

## 10. 运行时边界

MinecraftCaves 的内容生成入口是 `MinecraftCaveInfiniteWorld`。它与其他体素功能共享体素容器、世界坐标查询、类型配置和网格提取基础设施；洞穴密度生成、固定结构写入和流式网格调度由 MinecraftCaves 运行时负责。

## 11. 验证入口

编辑器提供两个与世界生成有关的验证入口：

- `Tools > Minecraft Caves > Validate Generation`：检查五种密度场均跨越零等值面、能够生成网格、相邻 chunk 使用绝对坐标，并检查 Combined 的空体积和连通性上限；
- `Tools > Minecraft Caves > Validate Infinite World`：检查 `32×256×32` 柱区块、
  XZ 半径 4、49 个唯一且近到远排序的偏移，以及负坐标柱的绝对坐标采样。

## 12. 相关源码

MinecraftCaves 生成代码：

- `Assets/Game/Runtime/MinecraftCaveNoise.cs`
- `Assets/Game/Runtime/MinecraftCaveDensity.cs`
- `Assets/Game/Runtime/MinecraftCaveVolumeGenerator.cs`
- `Assets/Game/Runtime/MinecraftCaveInfiniteWorld.cs`
- `Assets/Game/Runtime/MinecraftCaveGallery.cs`
- `Assets/Game/Runtime/MinecraftOreFeatureGenerator.cs`
- `Assets/Game/Runtime/MinecraftOreFeatureSettings.cs`
- `Assets/Game/Runtime/VoxelOreFeatureDefinition.cs`

被引用的 Voxel 代码：

- `Assets/Game/Runtime/Voxels/VoxelVolume.cs`
- `Assets/Game/Runtime/Voxels/VoxelChunkData.cs`
- `Assets/Game/Runtime/Voxels/VoxelColumnChunkData.cs`
- `Assets/Game/Runtime/Voxels/InfiniteVoxelWorld.cs`
- `Assets/Game/Runtime/Voxels/MarchingCubesMesher.cs`
- `Assets/Game/Runtime/Voxels/VoxelMeshData.cs`
