# MinecraftCaves 地形生成运行时说明

本文以 `InfiniteCaves.scene` 当前实际使用的运行时链路为准，说明关卡配置如何进入地形系统、洞穴密度和矿物如何写入柱数据、出生结构何时覆盖地形，以及柱数据如何流送并生成网格。文中只覆盖正式运行路径。

## 1. 运行时入口与配置来源

地形入口是场景中 `Caves` 对象上的 `MinecraftCaveInfiniteWorld`。组件启用时按以下优先级取得关卡：

1. 场景上的 `levelConfigurationOverride`；
2. `MissionGameLoop.CurrentLevelConfiguration`；
3. 后者在任务循环尚未建立时，回退到 `GameAssetCatalog.Current.Missions.DefaultLevel`。

如果最终没有 `LevelConfiguration`，或关卡没有 `WorldGeneration`，组件会输出错误并禁用，不会使用组件内的隐式默认值继续生成。配置一旦应用且世界已经初始化，也不能在同一实例上热切换。

`LevelConfiguration` 组合三类生成配置，其中地形直接读取 `MinecraftWorldGenerationConfiguration`。正式关卡的引用关系是：

```text
GameAssetCatalog
    └─ defaultLevel / 当前任务 LevelConfiguration
          └─ worldGeneration
                └─ MinecraftWorldGenerationConfiguration
```

当前主要资产：

- `Assets/Game/Config/GameAssetCatalog.asset`
- `Assets/Game/Config/Levels/FirstLevel.asset`
- `Assets/Game/Config/Worlds/DefaultWorldGeneration.asset`
- `Assets/Game/Config/Levels/CombatTestLevel.asset`
- `Assets/Game/Config/Worlds/CombatTestWorldGeneration.asset`

`InfiniteCaves.scene` 当前还序列化了一个关卡覆盖引用，并且它与目录中的默认关卡引用指向同一关卡。独立测试场景可以改用其他 `LevelConfiguration`，但世界参数仍必须通过关卡资产注入，不能直接序列化在 `MinecraftCaveInfiniteWorld` 上。

## 2. 两种实际生成模式

`MinecraftWorldGenerationMode` 当前有两个有效分支。

### 2.1 InfiniteCaves

正式洞穴关卡使用该模式。每根柱依次执行：

```text
绝对体素坐标 + worldSeed
    → Combined 洞穴密度
    → 全局粗格采样与三线性展开
    → 实体类型初始化
    → 顶/底边界基岩
    → 矿团替换
    → 出生结构覆盖（只在首次结构阶段）
    → Marching Cubes 分段网格
```

### 2.2 Superflat

战斗测试配置使用该模式。设 `H = SuperflatStoneHeight`：

```text
y < H   → density = 1，类型为基础实体类型
y >= H  → density = -1，类型为 Air
出生点  → (0, H, 0)
```

Superflat 不执行洞穴噪声、边界基岩、矿物或出生体素结构写入。它仍复用相同的柱流送、分段网格、材质和碰撞体路径。

## 3. 坐标、数据布局与密度约定

### 3.1 柱坐标

世界按 X/Z 二维柱流送。`VoxelColumnChunkData` 的固定尺寸是：

| 轴 | 样本数 | 说明 |
| --- | ---: | --- |
| X | 32 | 水平柱宽 |
| Y | 256 | 完整有限世界高度，范围 `0..255` |
| Z | 32 | 水平柱深 |

每柱包含 `32 × 256 × 32 = 262,144` 个 `float` 密度和同样数量的 `VoxelTypeId`。柱字典键只有 `(chunkX, chunkZ)`；兼容三维调用时 chunk Y 始终为 0。

世界坐标到柱坐标使用向负无穷取整的 floor division。因此 `x = -1` 属于 `chunkX = -1`、局部 X 为 31，相邻负坐标柱不会错位。

地形对象允许平移、旋转和缩放。玩家世界坐标先通过 `transform.InverseTransformPoint` 转为地形局部坐标，再除以 `voxelSize`；生成出的分段对象则作为地形对象子节点放置。

### 3.2 密度和类型

统一判定为：

```text
density >= isoLevel  且 type != Air  → 实体
density <  isoLevel                  → 空气
```

当前正式配置的 `isoLevel = 0`。写入负密度时，容器会把类型归一化为 `Air`；写入非负密度却传入 `Air` 时，会归一化为实体默认类型。地形生成本身明确写入 Stone、Ore 或 Bedrock，不依赖这一回退行为。

当前目录中的类型配置为：

| ID | 类型 | 地形用途 |
| ---: | --- | --- |
| 0 | Air | 洞穴空间 |
| 1 | Default | 容器和缺失材质定义时的实体回退类型 |
| 2 | Stone | 基础实体 |
| 3 | Ore | 默认矿团结果 |
| 4 | Bedrock | InfiniteCaves 的 Y=0 和 Y=255 边界 |

类型定义资产同时提供材质和耐久度。网格按 `VoxelTypeId` 生成 submesh，`VoxelTypeCatalog` 再按 submesh 类型解析对应材质。

## 4. 洞穴密度场

### 4.1 确定性噪声

`MinecraftCaveNoise` 使用绝对三维坐标和 seed 计算梯度 Perlin：

1. 对采样点所在立方体的 8 个角点做整数哈希；
2. 从 12 个三维梯度方向中选择梯度；
3. 用 `6t⁵ - 15t⁴ + 10t³` 平滑并沿 X/Y/Z 插值；
4. `FractalPerlin` 以默认 `lacunarity = 2`、`persistence = 0.5` 叠加 octave；
5. `NormalNoise` 合并两组不同 seed 和轻微错位的分形噪声，并把结果限制在 `[-1, 1]`。

噪声不读取 `UnityEngine.Random`，所以结果只由世界 seed 和绝对坐标决定，不受柱生成顺序或后台线程调度影响。

### 4.2 Combined 组合

正式洞穴柱只采样 `MinecraftCaveType.Combined`。它由四种参与组合的密度构造组成：

- Cheese：低频主体噪声形成大洞厅，Y 频率更高的平方分层项把部分空间填回实体；
- Spaghetti：两个扭曲隐式面的交线形成主隧道，并用独立噪声调节厚度和粗糙度；
- Noodle：更细的双隐式面交线，先经过 rarity 门控，只在许可区域形成捷径；
- Pillar：X/Z 变化较快、Y 变化较慢的实体密度，用于在空腔中填回纵向石柱。

三组更低频的 layout noise 决定这些结构能否出现：

```text
primaryGate = layoutThreshold - layoutA
roomGate    = max(primaryGate, layoutThreshold - layoutB)
rooms       = max(Cheese, roomGate)

corridors   = max(Spaghetti, primaryGate + corridorInset)

shortcutGate = max(
    primaryGate + shortcutInset,
    layoutThreshold + corridorInset - layoutC)
shortcuts    = max(Noodle, shortcutGate)

voids    = min(rooms, corridors, shortcuts)
Combined = max(voids, Pillar)
```

在“负值为空气”的约定下，`min` 合并空腔，`max` 施加布局限制或用 Pillar 填回实体。这些门控避免每种管道单独贯穿整个世界。

### 4.3 当前默认洞穴参数

`DefaultWorldGeneration.asset` 当前使用 seed `6667`，有效洞穴参数为：

| 分组 | 参数 | 值 |
| --- | --- | ---: |
| Cheese | frequency / threshold / layer strength | `0.001 / 0.02 / 0.1` |
| Spaghetti | frequency / thickness / warp / roughness | `0.025 / 0.13 / 0.38 / 0.035` |
| Noodle | frequency / thickness / rarity | `0.025 / 0.075 / -0.18` |
| Pillar | horizontal frequency / vertical frequency | `0.02 / 0.05` |
| Pillar | rarity / strength | `0.01 / 1` |
| Layout | frequency / threshold | `0.012 / 0` |
| Layout | corridor inset / shortcut inset | `0.04 / 0.08` |

这些值来自当前资产，而不是 `MinecraftCaveSettings` 字段声明处的新建对象默认值；运行时以关卡引用的资产为准。

## 5. 单柱地形与矿物生成

### 5.1 粗格采样

直接对 262,144 个点执行完整 Combined 计算成本过高。`MinecraftCaveDensityInterpolator` 使用全局对齐的粗格：

```text
X/Z 步长 = 2
Y 步长   = 4
粗格尺寸 = 17 × 65 × 17
粗采样数 = 18,785
```

粗格的 X/Z 原点由柱坐标乘 32 得到，Y 原点固定为 0。随后把粗格三线性展开为完整 `float[262144]`。相邻柱在边界上采样相同绝对坐标，因此正、负坐标区都保持连续。

### 5.2 类型、边界与数组提交

密度数组生成后：

1. `density >= 0` 的样本先标记为基础 Stone，其他样本标记为 Air；
2. Y=0 与 Y=255 的整层密度强制写为 `1`，类型写为 Bedrock；
3. 矿物生成器只替换允许的实心类型，不改变密度；
4. 完成的密度和类型数组由主线程通过 `AddChunkTakingOwnership` 直接交给 `InfiniteVoxelWorld`，不逐样本复制。

结构写入之后还会再次恢复顶、底边界基岩，防止结构覆盖破坏有限世界封口。

### 5.3 当前矿团规则

当前 `Ore.asset` 的有效设置是：

| 参数 | 当前值 |
| --- | ---: |
| 放置区域 | 每个 `16 × 16` XZ 区域 |
| 每区域尝试次数 | 32 |
| 基础放置概率 | 1 |
| 高度分布 | Trapezoid，Y=`1..254`，plateau=`0` |
| 矿团 size | 8 |
| 暴露空气时的丢弃概率 | 0.05 |
| 可替换类型 | Stone（ID 2） |
| 结果类型 | Ore（ID 3） |

基础概率还会乘深度曲线：浅层倍率 `0.25`、深层倍率 `1`、指数 `1.35`。Y=0 被视为最深处，Y=255 被视为最浅处，因此矿物越深越容易通过放置概率检查。

每次尝试由 `worldSeed + feature seedSalt + regionX/Z + attempt` 派生确定性随机序列。算法沿一条短轴建立若干相互重叠的球，删除被其他球完全包含的球，再把球内、密度为实体且类型可替换的样本改为 Ore。若样本与六邻域空气相邻，则按坐标哈希和丢弃概率独立决定是否跳过。

目标柱会重放可能影响自己的相邻 16×16 区域，只写自己的类型数组；因此跨柱矿团不依赖哪一根柱先生成，也不会因并发顺序产生接缝。

## 6. 出生点与结构覆盖

### 6.1 洞穴出生候选

InfiniteCaves 用 `worldSeed ^ 0x51F15EED` 初始化确定性随机数：

1. 首次检查 `(0, 159, 0)`；
2. 之后最多尝试 2,399 个点，X/Z 范围为 `[-72, 72]`，Y 范围为 `95..223`；
3. 中心 Combined 密度必须小于 `-0.035`；
4. 中心和六个轴向距离 2 的点都必须为空。

第一个满足条件的点成为结构锚点；若没有候选完全合格，则使用所有候选中密度最低的点。玩家实际目标位置还会加上结构资产的 `playerSpawnOffset`。

### 6.2 初始加载范围

初次生成不会立刻加载完整半径，而是加载出生柱周围 `3 × 3` 的 9 根柱。若 `SpawnPointStructureRule` 跨越该范围，还会把结构影响到的柱加入 required set。

`placeViewerInCave` 启用时：

- 暂时禁用玩家 `CharacterController`；
- 把玩家保持在目标出生姿态；
- 暂时把全局重力设为零；
- 等初始地形、结构、网格和碰撞体全部就绪后再恢复控制器与原重力。

### 6.3 结构阶段

初始 required set 的所有柱完成后，流水线才从 Terrain 进入 Structures。当前规则使用 `SpawnShelter.asset`：尺寸 `11 × 6 × 11`，anchor `(5,1,5)`，玩家偏移 `(0,1.25,0)`。

结构阶段的顺序是：

1. 对齐场景中的 `SpawnPointSceneStructure`；
2. 在出生柱四个正交相邻柱中寻找最近的可站立洞穴点；
3. 让出生舱朝向目标，并把目标交给出口通道；
4. 把固定结构资产的密度和类型覆盖到世界柱数据；
5. 恢复 Y=0/Y=255 的 Bedrock；
6. 根据出生舱实际 Renderer 范围雕刻舱体净空、出口通道和向世界顶部的落地竖井；
7. 稳定出生舱周围的落脚地面，并清理对应头顶空间；
8. 最后才把所有受影响网格分段加入构网队列。

结构只在世界初始化后的首次结构阶段应用一次。后续玩家跨柱触发的流送不会重复覆盖已经修改过的世界数据。

## 7. 流送状态与生成生命周期

### 7.1 正常流送范围

初始区域 Ready 后，系统扩展到玩家周围 XZ 欧氏半径 4 的圆盘：

```text
dx² + dz² <= 4²
```

共有 49 个偏移，并按距离平方从近到远排序。Y 不参与流送寻址，每个偏移都代表完整的 256 高柱。

玩家进入新柱时会重建 required set：

- 已有柱数据直接复用；
- 缺失柱进入地形生成队列；
- 离开范围的在途地形任务会收到取消请求；
- 离开范围的网格对象和 Mesh 每帧最多销毁 2 个；
- 对应柱中的非玩家动态刚体会被停用并保存速度，柱网格重新完整建立后再恢复。

`InfiniteVoxelWorld` 中已经提交的柱数据在组件存活期间不会因离开可视范围而删除。因此返回旧区域时不重新计算密度和矿物，但会重新生成已经卸载的网格。这也意味着已挖掘或放置的样本修改可以保留；代价是探索范围越大，柱数据内存会持续增长。

### 7.2 地形后台任务

缺失柱通过 `Task.Run` 生成，最多同时运行 `maxConcurrentGenerationJobs` 个任务。任务只持有线程安全快照：seed、密度场、类型 ID、矿物设置、深度曲线、模式和高度；不会读取 Unity 场景对象或修改世界字典。

主线程在 `Update` 中提交已完成结果。若结果对应柱已离开 required set，或同坐标已存在数据，则丢弃结果；被取消但又重新进入范围的柱会重新排队。

## 8. Marching Cubes 与网格任务

### 8.1 分段和邻域

每根 256 高数据柱拆成 8 个网格分段，每段高 32。一个分段处理 `32 × 32 × 32` 个 cell，需要捕获 `33 × 33 × 33` 个 `VoxelSample`，包括当前柱以及 +X、+Z、+X+Z 邻柱的边界样本。

流送范围内需要采样的邻柱尚未生成时，该分段暂不派发。required set 之外或世界 Y 范围之外的缺失样本按实体处理：水平缺失类型使用基础实体类型，垂直越界类型使用 Bedrock。这样可视范围边缘和有限世界上下边界保持封闭。

新柱从“缺失且按实体处理”变成真实数据后，会使自身、-X、-Z、-X-Z 四根柱的网格可能过期，因此这些柱的分段会强制重建。

### 8.2 当前异步构网流程

普通流送网格采用三段式流程：

1. 主线程每帧最多捕获 1 个分段的 `VoxelSample` 快照，缓冲区来自 `ArrayPool`；
2. 后台任务对快照执行 Marching Cubes，最大并发数为 `max(1, maxConcurrentGenerationJobs / 2)`；
3. 主线程每帧最多提交 `meshesBuiltPerFrame` 个已完成结果，创建或替换 Unity `Mesh`、Renderer 和可选 Collider。

每个分段有递增版本号。后台结果返回时，如果分段已再次变脏、离开范围或版本不匹配，结果不会提交。玩家挖掘/放置触发的高优先级重建则绕过普通阶段门和帧预算，同帧同步重建受影响分段，避免继续碰撞旧网格。

### 8.3 多类型表面与材质

Mesher 先收集快照中所有非空气实体类型，再为每个类型构造自己的二值场。不同实体类型交界处会分别生成略微内缩的表面，避免完全共面；三角形按类型写入排序后的 submesh。

顶点位置可选边中点或密度插值，当前配置使用 `DensityInterpolated`。网格数据同时生成投影 UV、平滑法线和 tangent；顶点数超过 65,535 时自动使用 32 位索引。材质从 `VoxelTypeCatalog` 按 submesh 类型解析，缺失定义时使用运行时创建的 Lit 回退材质。

## 9. 启动阶段与就绪条件

`MinecraftCaveGenerationStage` 的实际状态机为：

```text
None
  → Terrain：required set 的柱数据全部生成并提交
  → Structures：首次启动时执行出生结构和地形净空
  → Meshes：所有柱的 8 个分段按离玩家高度由近到远构建
  → Ready：required set 中每根柱的所有分段都已评估
```

空分段也会记为“已构建”，但不会创建 GameObject。只有一根柱的 8 个分段都完成，系统才认为该柱碰撞体完整，并允许在该柱恢复刚体或生成依赖地面的内容。

初次 Ready 后释放玩家、恢复重力，并把流送范围从 3×3 扩大到半径 4。之后跨柱会重新经历 Terrain/Meshes；结构阶段因为已经应用过而跳过。

加载进度中 Terrain 占 72%，Meshes 占 28%。Structures 本身显示为 72% 边界值；Ready 为 100%。

## 10. 当前配置快照

`DefaultWorldGeneration.asset` 当前与地形直接相关的运行参数：

| 参数 | 值 |
| --- | ---: |
| generationMode | InfiniteCaves |
| worldSeed | 6667 |
| placeViewerInCave | true |
| maxConcurrentGenerationJobs | 1 |
| meshesBuiltPerFrame | 1 |
| voxelSize | 0.42 |
| isoLevel | 0 |
| vertexPlacement | DensityInterpolated |
| generateColliders | true |
| baseSolidVoxelType | Stone（ID 2） |
| bedrockVoxelType | Bedrock（ID 4） |
| oreFeatures | `Ore.asset` |
| spawnPointStructureRule | 启用，`SpawnShelter.asset`，offset `(0,0,0)` |

`CombatTestWorldGeneration.asset` 当前使用 Superflat、seed `114514`、石层高度 10、最大并发地形任务 4；其他渲染和类型配置与默认世界相同，但不会进入洞穴、矿物和结构分支。

## 11. 修改参数时必须保持的约束

- 洞穴连续性依赖绝对世界坐标和全局对齐粗格；不要用柱内局部坐标重新起噪声。
- 后台地形任务只能使用值快照和纯数据，不能访问 `GameObject`、`Mesh`、`ScriptableObject` 或世界字典。
- Bedrock 必须在矿物和首次结构覆盖之后仍封住 Y=0/Y=255。
- 新增矿物只能替换显式配置的实体类型；跨柱形状必须由区域坐标和 attempt seed 重放，而不是依赖生成顺序。
- 分段网格读取 +X/+Z 邻域；修改边界样本时必须把反方向相邻分段一并标脏。
- Unity Mesh、Renderer、Collider 的创建、替换和销毁必须留在主线程。
- 若调整柱尺寸、世界高度或网格分段高度，必须同步检查粗格整除关系、8 段覆盖、出生高度范围、边界基岩和测试断言。

## 12. 代码与验证位置

运行时主链路：

- `Assets/Game/Runtime/MinecraftCaveInfiniteWorld.cs`
- `Assets/Game/Runtime/MinecraftWorldGenerationConfiguration.cs`
- `Assets/Game/Runtime/MinecraftCaveNoise.cs`
- `Assets/Game/Runtime/MinecraftCaveDensity.cs`
- `Assets/Game/Runtime/MinecraftCaveDensityInterpolator.cs`
- `Assets/Game/Runtime/MinecraftOreFeatureGenerator.cs`
- `Assets/Game/Runtime/MinecraftOreFeatureSettings.cs`
- `Assets/Game/Runtime/VoxelOreFeatureDefinition.cs`
- `Assets/Game/Runtime/MinecraftCaves/CardinalCaveConnectionSearch.cs`
- `Assets/Game/Runtime/MinecraftCaves/SpawnPointSceneStructure.cs`

体素与网格基础设施：

- `Assets/Game/Runtime/Voxels/VoxelColumnChunkData.cs`
- `Assets/Game/Runtime/Voxels/InfiniteVoxelWorld.cs`
- `Assets/Game/Runtime/Voxels/MarchingCubesMesher.cs`
- `Assets/Game/Runtime/Voxels/VoxelMeshData.cs`
- `Assets/Game/Runtime/Voxels/VoxelStructureAsset.cs`
- `Assets/Game/Runtime/Voxels/VoxelTypeDefinition.cs`
- `Assets/Game/Runtime/Voxels/VoxelTypeCatalog.cs`

与这条链路直接相关的 EditMode 测试位置：

- `Assets/Game/Tests/Editor/VoxelColumnChunkTests.cs`
- `Assets/Game/Tests/Editor/MinecraftOreFeatureGeneratorTests.cs`
- `Assets/Game/Tests/Editor/MinecraftOreConfigurationAssetTests.cs`
- `Assets/Game/Tests/Editor/VoxelTypeMarchingCubesTests.cs`
- `Assets/Game/Tests/Editor/VoxelStructureTests.cs`
- `Assets/Game/Tests/Editor/SpawnPointSceneStructureTests.cs`
- `Assets/Game/Tests/Editor/WorldAndEffectTests.cs`
