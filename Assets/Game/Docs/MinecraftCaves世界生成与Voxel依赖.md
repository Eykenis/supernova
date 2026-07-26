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
后台任务生成单个 chunk 的 float[32³]
        ↓
主线程提交到 InfiniteVoxelWorld / VoxelChunkData
        ↓
MarchingCubesMesher.BuildChunk
        ↓
VoxelMeshData.CreateMesh
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

### 4.1 世界 chunk

`FillChunk` 从 `VoxelChunkData.OriginX/Y/Z` 取得绝对原点，再遍历局部 `0..31`：

```text
worldPosition = chunkOrigin + localPosition
density[x,y,z] = SampleFeatureDensity(worldPosition, type)
```

绝对坐标采样保证相邻 chunk 不会因为各自局部坐标归零而产生噪声接缝，也保证负 chunk 坐标得到同一连续密度场。

无限世界的后台生成函数执行相同逻辑，同时写入独立的
`float[32768]` 和 `VoxelTypeId[32768]`。实心样本先分配为配置的基岩类型，再由
普通矿团阶段替换类型；工作线程不会访问世界字典和 Unity 资产。

### 4.2 展示体积与正式世界的区别

`FillDisplayVolume` 只服务于 `MinecraftCaveGallery`：它把一个 `32³` 体积居中到原点，并额外与盒形容器和可选剖切平面组合，以便独立观察各密度类型。

正式无限世界使用 `SampleFeatureDensity(..., Combined)`，没有展示盒和剖切面。

## 5. 无限三维 Chunk 流送

`MinecraftCaveInfiniteWorld` 固定维护玩家周围欧氏半径为 4 的三维 chunk 球：

```text
dx² + dy² + dz² <= 4²
```

满足条件的偏移共 257 个，并在初始化时按距离平方由近到远排序。这里 Y 轴与 X/Z 完全同等参与加载，世界不是二维柱状流送。

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

1. 分配一个 `float[VoxelVolume.VoxelCount]` 和一个
   `VoxelTypeId[VoxelVolume.VoxelCount]`；
2. 根据 chunk 坐标计算绝对原点；
3. 遍历 `32³` 个采样点并计算 `Combined` 密度；
4. 为实心样本分配基岩类型，并用确定性
   `MinecraftOreFeatureGenerator` 写入普通矿团类型；
5. 每完成一个 Z 切片以及每次矿团尝试时检查取消令牌；
6. 返回坐标、密度和类型数组，不访问 `GameObject`、`Mesh`、世界字典或
   `ScriptableObject`。

主线程在 `Update` 中轮询已完成任务。只有结果对应的 chunk 仍在 required set 且尚未生成时才提交：

1. 调用 `InfiniteVoxelWorld.EnsureChunk`；
2. 通过 `SetSample` 把密度和类型逐项复制到 `chunk.Data`；
3. 标记受该新密度影响的网格。

离开加载范围的在途任务不会立即单独取消；它完成后若已不在 required set，结果会被丢弃。组件禁用、销毁或应用退出时会取消整个生成令牌并清理运行时状态。

## 7. Chunk 边界与网格更新

MinecraftCaves 调用 `MarchingCubesMesher.BuildChunk(world, coordinate, isoLevel, voxelSize)`。一个 chunk 保存 `32³` 个采样点，但该重载负责 `32³` 个 cell，因此还会读取正方向边界上的相邻采样点，合计预取 `33³` 个密度值。

未生成邻居按实体密度 `isoLevel + 1` 处理。这样加载边界暂时封闭，不会把未知区域当空气并生成虚假的开放边界。

新 chunk 从“未知且按实体处理”变为真实密度后，可能改变它自己以及负方向八分体中的 7 个相邻网格：

```text
affected = generatedChunk - (dx, dy, dz)
dx, dy, dz ∈ {0, 1}
```

系统只把 required set 内且已有体素数据的这些 chunk 加入 dirty set。`dirtyMeshes` 去重，`meshesBuiltPerFrame` 限制主线程每帧最多重建多少个网格。

重建时先销毁该坐标的旧运行时对象，然后：

1. 运行 Marching Cubes；
2. 空网格只记录为已构建，不创建 GameObject；
3. 非空网格创建 Mesh、MeshFilter 和 MeshRenderer；
4. 根据设置选择是否创建 MeshCollider；
5. chunk 对象放到 `coordinate * 32 * voxelSize` 的局部位置。

## 8. 出生点选择

启用 `placeViewerInCave` 时，系统用世界种子派生一个确定性 `System.Random`：

- 第一次检查体素原点 `(0,0,0)`；
- 随后最多在 X/Z `[-72,72]`、Y `[-48,48]` 中尝试随机点，总尝试数为 2400；
- 候选点密度必须小于 `-0.035`；
- 候选中心及六个轴向、距离为 2 的采样点必须全部为空。

找到合格点立即作为出生体素；若没有找到，则使用所有候选中密度最低的位置。

## 9. 实际引用的 Voxel 内容

MinecraftCaves 世界生成只使用以下 `Supernova.Voxels` 能力。

### 9.1 `VoxelVolume`

- 固定尺寸 `32 × 32 × 32`；
- 连续 `float[32768]` 密度和 `VoxelTypeId[32768]` 类型存储；
- 提供局部 `(x,y,z)` 索引；
- Gallery 直接创建该类型，无限世界通过 `VoxelChunkData` 间接持有。

### 9.2 `VoxelChunkData`

- 保存三维 chunk 坐标；
- 提供 `OriginX/Y/Z = ChunkCoordinate * 32`；
- 保存对应的 `VoxelVolume`；
- 用于把局部采样坐标准确映射到绝对世界坐标。

### 9.3 `InfiniteVoxelChunk` 与 `InfiniteVoxelWorld`

- `InfiniteVoxelWorld` 用 `Dictionary<Vector3Int, InfiniteVoxelChunk>` 缓存已提交的 chunk；
- `EnsureChunk` 创建或返回指定 chunk；
- `TryGetChunk` 用于流送状态和网格构建判断；
- `GetDensityOrDefault` 为跨 chunk 网格采样提供世界密度；
- `WorldToChunk` 使用 floor division 处理负坐标。

`InfiniteVoxelChunk` 创建时默认填充正密度 `1`，但 MinecraftCaves 在提交后台
结果时会用生成的完整密度和类型数组覆盖它。

### 9.4 `MarchingCubesMesher`

- 使用固定 `256 × 16` case 表；
- 每个 cell 读取 8 个角点并形成 8-bit case；
- case `0` 和 `255` 跳过；
- 12 条边使用固定中点，不做动态密度插值；
- `BuildChunk` 预取并复用 `33³` 密度缓存；
- 当前静态密度缓存不支持多个线程并发调用，因此网格构建保留在主线程串行执行。

### 9.5 `VoxelMeshData`

- 保存顶点和三角形索引；
- 创建 Unity Mesh 时自动选择 16/32 位索引；
- 上传三角形后重新计算法线和 Bounds。

## 10. 运行时边界

MinecraftCaves 的内容生成入口是 `MinecraftCaveInfiniteWorld`。它与其他体素功能共享体素容器、世界坐标查询、类型配置和网格提取基础设施；洞穴密度生成、固定结构写入和流式网格调度由 MinecraftCaves 运行时负责。

## 11. 验证入口

编辑器提供两个与世界生成有关的验证入口：

- `Tools > Minecraft Caves > Validate Generation`：检查五种密度场均跨越零等值面、能够生成网格、相邻 chunk 使用绝对坐标，并检查 Combined 的空体积和连通性上限；
- `Tools > Minecraft Caves > Validate Infinite World`：检查 `32³` chunk、半径 4、257 个唯一且近到远排序的偏移，以及负坐标 chunk 的绝对坐标采样。

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
- `Assets/Game/Runtime/Voxels/InfiniteVoxelWorld.cs`
- `Assets/Game/Runtime/Voxels/MarchingCubesMesher.cs`
- `Assets/Game/Runtime/Voxels/VoxelMeshData.cs`
