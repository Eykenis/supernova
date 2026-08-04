# Jigsaw 结构生成算法

> 本文完整描述 Supernova 当前的 Jigsaw 结构生成实现：从选址、布局、落地到缓存与校验。
>
> 面向程序读者。若只想配置一个新结构而不改代码，请读同目录的 `Jigsaw结构配置手册.md`。
>
> 算法背景与 Minecraft 原版对照见 `Guideline/Minecraft生成式结构算法指南.md`。

## 1. 总览

一类结构由一个 `JigsawStructureFeatureDefinition` 资产描述，内含若干
`JigsawPieceDefinition` 模块。世界配置只持有结构资产列表，不为矿洞、堡垒等
具体内容增加顶层类型。

```text
MinecraftWorldGenerationConfiguration.jigsawStructures
  -> JigsawStructureFeatureDefinition          结构族
       placement / layout policy / materials
       -> JigsawPieceDefinition[]              模块
            selection constraints
            procedural geometry 或 voxel template
            -> JigsawConnectorDefinition[]     显式 socket
            -> JigsawProcessorDefinition[]     落地处理器
```

运行时分四个阶段，每个阶段的输出都是下一阶段的纯函数输入：

| 阶段 | 输入 | 输出 | 代码 |
|---|---|---|---|
| 1 选址 Placement | worldSeed + 结构 placement 参数 | 候选起点 `Placement` | `JigsawPlacementService` |
| 2 布局 Layout | 候选起点 + 模块图 | `Piece[]` 轻量布局 | `JigsawStructureGenerator.BuildBestLayout` |
| 3 裁剪 Clip | 布局 + 当前体素柱坐标 | 与该柱相交的 `Piece[]` | `LayoutCacheEntry.GetPiecesForColumn` |
| 4 落地 Rasterize | 相交 piece | 写入 `densities[] / types[]` | `ApplyLayoutToColumn` |

### 1.1 确定性契约

整条链路只依赖三项输入：

```text
worldSeed + feature.ContentHash + region 坐标
```

因此：

- 同一世界种子下，同一区域的布局永远一致；
- 布局**不依赖区块流送顺序**——先加载哪一柱都得到相同结果；
- 缓存只是性能优化，不是正确性前提；清空缓存后必须能由种子完整重放。

这条契约是所有设计取舍的前提。下文凡出现"必须"，多半是为了守住它。

## 2. 阶段一：选址

`JigsawPlacementService.CollectPlacements` 是统一入口，按结构配置的
`placementStrategy` 分派。它返回所有**可能影响给定水平窗口**的候选点，
窗口会先向外扩张 `maxHorizontalDistance`，因为布局可以从中心向外延伸。

### 2.1 RandomSpread（默认）

世界被切成边长 `regionSizeInChunks * 32` 体素的方形区域，每个区域最多产出
一个候选：

```text
random = DeterministicRandom(worldSeed, seedSalt, regionX, regionZ)
if random.NextDouble() >= placementChance: 该区域无结构
margin = maxHorizontalDistance
centre.x = regionX * regionSize + margin + random.NextInt(regionSize - 2*margin)
centre.z = regionZ * regionSize + margin + random.NextInt(regionSize - 2*margin)
centre.y = minFloorHeight + random.NextInt(maxFloorHeight - minFloorHeight + 1)
```

`margin` 保证候选点距区域边界至少 `maxHorizontalDistance`，于是一座结构的
所有 piece 都落在**拥有它的那个区域**内。这让"哪些区域可能影响本柱"成为
一次简单的整数除法，无需搜索。由此产生一条硬约束：

```text
regionSizeInChunks * 32 > 2 * maxHorizontalDistance
```

违反时构造 `JigsawStructureFeatureSettings` 直接抛异常。

### 2.2 ConcentricRings

用于"从世界中心向外探索、发现密度可预期"的结构（原版末地要塞范式）。
`ringStructureCount` 个候选点分布在 `ringCount` 个同心环上：

- 第 n 环半径 `(n+1) * ringDistanceInChunks * 32`，叠加 `±ringSpreadInChunks` 的径向抖动；
- 环内名额按 `(ring+1) / Σ(1..ringCount)` 比例分配，外环名额更多，避免越远越稀疏；
- 环内按近似等角分布，角度带种子抖动（每格 ±25% 角距）；
- 最后一环吃掉所有剩余名额，保证总数精确等于 `ringStructureCount`。

整套候选点按 `(ContentHash, worldSeed)` 缓存（上限 32 组），并按 512 体素
的格子分桶，于是单柱查询只扫描邻近桶而非全表。

注意：环候选是**绝对世界坐标**，不归属任何 region，因此 2.1 的
`regionSize > 2 * maxHorizontalDistance` 检查对该策略不适用。

### 2.3 Structure Set 竞争

多个结构可以填写同一个 `structureSetId`，此时它们竞争同一个候选格：

```text
totalWeight = Σ weight(同 set 的所有结构)
random = DeterministicRandom(worldSeed, hash(structureSetId), regionX, regionZ)
roll = random.NextInt(totalWeight)
按权重线性扫描，命中者独占该格
```

随机种子取自 **set 名称**而非任一成员，所以胜者与结构在世界配置里的
排列顺序无关；增删无关结构不会改变已有格子的归属。未填写
`structureSetId` 的结构永远独占自己的格子，不参与任何竞争。

## 3. 阶段二：布局

### 3.1 Frontier piece graph

`BuildLayoutAttempt` 是一个 FIFO frontier 扩张：

```text
1. 在候选点建立 start piece，随机选四向之一作为朝向
2. 激活 start 的输出 socket，压入 frontier 队列
3. while frontier 非空 且 pieces.Count < maxPieces:
     connector = frontier.Dequeue()
     if connector.Depth > maxDepth: 丢弃
     for attempt in 0..connectorPlacementAttempts:
         moduleIndex = PickPieceIndex(...)        // 见 3.2
         if 无候选:
             若主池已试尽且存在 fallbackPool: 切换到 fallback 重试
             否则该分支终止
         candidate = TryCreateCandidate(...)      // 见 3.3，几何 + 对齐
         if CanAddPiece(...):                     // 见 3.4，碰撞
             提交 piece，其输出 socket 入队
             break
```

一个 socket 试满 `connectorPlacementAttempts` 次仍失败就放弃该分支——
frontier 中其他 socket 不受影响，结构会向别处生长。

### 3.2 模块选择

`PickPieceIndex` 分三级优先，逐级回落：

**第 0 级 — 强制首模块。** 若 `connector.Depth == 1` 且结构配置了
`firstPieceId`，且该模块此刻合法，直接返回它。用于"矿洞起点必须先接一段
走廊"这类形态控制。

**第 1 级 — 必需目标。** 计算尚未满足 `minimumCount` 的模块。当满足下述
任一条件时，它们进入**优先池**，只在优先池内按权重抽取：

- `capacityUrgent`：`maxPieces - 已放置 <= 未满足的最小数量总和`，即容量将耗尽；
- `connector.Depth >= requiredByDepth`（`requiredByDepth` 为 0 时取 `maxDepth`）。

这让"传送门房间必须存在"这类目标在深度足够或名额告急时抢占选择权，而不是
等到图末尾硬塞进不合适的位置。

**第 2 级 — 常规加权抽取。** 在所有合法候选中按 `weight` 线性扫描抽取。

三级共用同一套 `IsEligible` 过滤：

```text
非 start piece
weight > 0
poolId == connector 的目标池
minimumGraphDepth <= depth <= maximumGraphDepth
maximumCount == 0 或 已生成数 < maximumCount
allowConsecutive 或 父 piece 的模块 != 自身
```

外加 `HasCompatibleInput`：候选必须存在一个 input socket 与该 connector
名称互配。已试过的模块记入 `excludedModules`，同一 socket 不会重复尝试。

`maximumCount` 是**硬约束**，任何优先级都不能突破。`minimumCount` 是
**布局有效性目标**，靠 3.5 的整图重试来提高达成率。

### 3.3 Socket 匹配与对齐

只要模块配置了至少一个 `JigsawConnectorDefinition`，它就完全使用显式
socket，`connectorPattern` 被忽略（后者仅为旧资产的兼容路径）。

名称匹配是**双向**的，`*` 为通配：

```text
输出.targetName 匹配 输入.socketName
且 输入.targetName 匹配 输出.socketName
```

对齐过程：

1. 枚举候选模块所有兼容的 input socket，用蓄水池抽样等概率取一个
   （`random.NextInt(++matchingCount) == 0`），保证与枚举顺序无关；
2. 由 `direction = (connector.Direction + 2 - input.Face) & 3` 定出子 piece
   朝向，使子 socket 的朝向与父 socket 相反；
3. 按该朝向生成几何（Box / Passage / Template），得到临时 piece；
4. 计算子 input socket 在临时 piece 上的世界位置 `boundary`；
5. 整体平移 `connector.Position - boundary`，使两个 socket 严格贴合。

`GetAuthoredConnectorBoundary` 按模块类型分三种算法：

- **Template**：socket 自带模板局部坐标，直接绕 piece 朝向旋转该坐标。
  这是最精确的一种——标记知道自己贴在哪个体素上。
- **Box（Room / Crossing）**：由 `face` 定出所在墙面，沿墙面法线取半跨，
  再叠加 `lateralOffset` 横移、`verticalOffset` 抬高。
- **Passage（Corridor / Stairs）**：`Forward`/`Back` 取通道两端；
  `Right`/`Left` 取侧墙，沿通道位置由 `alongOffset` 决定（-1 表示中点）。

父输出门洞与子输入门洞**都**会记入各自 piece 的 `Openings` 列表，因此
Masonry 外壳会在两侧同时雕通，走廊侧分支不会出现"几何相邻但被墙隔开"。

### 3.4 碰撞

`CanAddPiece` 依次检查：

1. **世界边界**：`Bounds.MinY > 1` 且 `Bounds.MaxY < 255`；
2. **水平半径**：包围盒完全落在 `placement.Centre ± maxHorizontalDistance` 内；
3. **AABB 相交**：候选包围盒外扩 `collisionPadding`（仅水平），用 16 体素
   的 spatial hash 查出邻近 piece，再做精确整数 AABB 判定。父 piece 被跳过，
   因为它本就应当在连接面相接。

**处理器不参与碰撞。** 一根伸向地下 24 格的支柱若计入包围盒，会造成大量
无谓的拒绝。`ProcessorDownwardReach` / `ProcessorUpwardReach` 只用于落地
阶段自截断，不改变任何布局决策——这一点由测试
`Processors_DoNotAffectLayoutCollisionDecisions` 守住。

### 3.5 整图重试

单次 frontier 扩张不保证满足所有 `minimumCount`（空间可能确实放不下）。
`BuildBestLayout` 用派生种子重建整张图：

```text
for attempt in 0..layoutAttempts:
    layout = BuildLayoutAttempt(seed 派生自 attempt)
    deficit = Σ max(0, minimumCount - 实际数量)
    if deficit < 最优 或 (deficit 相同且 piece 更多): 记为最优
    if deficit == 0: 提前返回
return 最优布局
```

选择顺序是"缺失最少优先，其次 piece 更多"。当空间完全不可行时返回缺失
最少的布局，而**不是**突破碰撞边界强塞模块——这类配置问题应由资产校验和
多种子批量测试提前发现，而非在运行时破坏结构完整性。

## 4. 阶段三：裁剪

布局构造完成后，`LayoutCacheEntry` 预先建立"体素柱坐标 -> 相交 piece[]"
索引：每个 piece 的包围盒覆盖哪些 32×32 柱，就登记进对应桶。

于是单柱生成只需一次字典查询即可拿到相关 piece，不必遍历整座结构。这也是
"先求布局、再按柱裁剪"的直接收益——跨柱结构与流送顺序彻底解耦。

## 5. 阶段四：落地

### 5.1 四遍写入

`ApplyLayoutToColumn` 对**所有**相交 piece 执行四遍，一遍完成后才进入下一遍：

| Pass | 作用 |
|---|---|
| `Shell` | 写外壳实体体素（墙、地板、天花板） |
| `Air` | 雕空内部，并按 `Openings` 打通门洞 |
| `Accent` | 写装饰体素（书架、立柱、牢栅、传送门框、支撑架） |
| `Processor` | 落地处理器（支柱、地基、清顶、风化） |

分遍的关键在于**顺序无关**：若逐 piece 完成全部遍次，相邻 piece 的 Shell
会覆盖上一个 piece 已雕好的 Air，把门重新封死。全局分遍保证任何 piece 的
外壳都先于任何 piece 的雕空写入。`Processor` 排在最后，使支柱和清顶看到的
是**完工后**的结构，而不是半成品。

### 5.2 几何求值

- **Excavated**：不写 Shell，只在内部雕空，再按 `Decoration` 写 accent。
  适合矿洞——直接在实体地形里挖出来的感觉。走廊地板是**条件写入**：仅当
  该处原本不是实体时才补地板，于是穿过实体山体时不会画出多余楼板。
- **Masonry**：完整三遍。Box 的边界六面为壳，内部为空；Passage 的地板、
  天花板、两侧墙为壳，中间为空。适合堡垒——独立砌出来的建筑。

Stairs 的地板高度沿通道按 `InterpolateFloor` 线性插值，`GetFloorY(piece, along)`
统一了平通道与楼梯的取值。

### 5.3 Voxel Template

模块可以指定 `voxelTemplate` 替代程序化几何。`TryEvaluateTemplateSample`
把世界坐标逆旋转回模板局部坐标后采样：

```text
local.x = Δx * right.x + Δz * right.z + anchor.x
local.y = worldY - origin.y + anchor.y
local.z = Δx * forward.x + Δz * forward.z + anchor.z
```

Shell 遍写入模板中的实体样本（**保留模板自带的体素类型**，而非结构主色）；
Air 遍在 `templateWritesAir` 打开时写入模板中的空气样本，并优先打通
`Openings`。模板不参与 Accent 遍。

模板可以自带 socket 标记（`VoxelStructureSocket`）。模块若未自行配置
connector，则直接继承模板里的标记，于是 socket 不会与它所属的几何脱节。

### 5.4 处理器

处理器在布局阶段之外运行，读取**已经写好**的体素场：

| Kind | 行为 |
|---|---|
| `SupportToGround` | 从 piece 底面向下写实体，**遇到已有地形即停**。桥、平台由此获得连续支柱。`perimeterOnly` 时只在footprint 边缘落柱。 |
| `FoundationFill` | 向下写固定厚度的地基板，不探测地形。 |
| `ClearAbove` | 向上雕空固定高度，防止地形把结构封顶。 |
| `Weathering` | 把一部分体素替换为 accent 调色，产出石砖混色。**只改本结构写过的体素**（类型等于 primary 或 accent），绝不误伤周边地形或矿脉。 |

`inset` 从包围盒向内收缩 footprint；`chance` 是逐体素概率。

概率掷点键取**世界坐标**而非迭代计数：

```text
hash = Mix(worldX*P1 ^ worldY*P2 ^ worldZ*P3 ^ processor.Salt ^ piece.ModuleIndex)
apply if (hash >> 11) * 2^-53 < chance
```

这样同一个体素无论从哪一柱、第几次被访问到，结果都相同。`Salt` 由处理器
ID 经 FNV 派生，不用 `string.GetHashCode`（后者跨进程不保证稳定，会破坏
第 1.1 节的确定性契约）。

### 5.5 与体素分组的关系

结构常同时使用 primary 与 accent 两种体素类型（风化处理器更是刻意混用）。
Marching Cubes 曾按体素**类型**分别抽取等值面，并在实体/实体类型交界处
内缩 0.05 以避免 z-fighting，这在结构内部表现为坑洼。

现在体素类型归入 `VoxelGroup`（Structure / Stone / Ore），等值面**按组**
抽取：同组类型连成一体，不产生接缝；跨组仍然内缩分界。submesh 依然按
类型划分，所以每种调色板保留自己的材质。详见 `VoxelGroup.cs`。

## 6. 缓存与性能

### 6.1 布局缓存

缓存键：

```text
feature.ContentHash + worldSeed + regionX + regionZ + placementCentre
```

`ContentHash` 覆盖结构参数、材质 ID、全部模块、配额、尺寸、socket、
processor 以及模板内容哈希，因此编辑任何资产字段都不会错误复用旧布局。

容器是 `ConcurrentDictionary<key, Lazy<layout>>`：多个后台区块任务同时
请求同一区域时只构建一次（`LazyThreadSafetyMode.ExecutionAndPublication`）。
缓存上限 512 条，按插入顺序淘汰，避免无限世界流送导致无界增长。

### 6.2 空间索引

三处索引，各解决一个 O(n²) 问题：

- 布局碰撞：16 体素 cell 的 spatial hash，只比对邻近 piece；
- 柱裁剪：缓存条目预建"柱坐标 -> piece[]"；
- 环候选：512 体素 cell 分桶。

### 6.3 线程模型

世界生成跑在后台任务上，因此所有配置都先快照成不可变结构体：
`JigsawStructureFeatureSettings` / `JigsawPieceSettings` /
`JigsawConnectorSettings` / `JigsawProcessorSettings`，数组一律 `Clone()`。
ScriptableObject 本体永不暴露给 worker 线程。同理，体素分组也快照为
`VoxelGroupMap`（纯 int 数组）而非运行时查 catalog。

## 7. 校验

`JigsawStructureValidator.Validate` 供 Inspector、测试与工具共用：

**Error（结构无法正常生成）**

- start piece 没有任何输出 socket；
- template piece 既无自身 connector、模板也无 socket 标记；
- 必需模块（`minimumCount > 0`）的深度区间与 `maxDepth` 无交集；
- 同一模块内 processor ID 重复；
- RandomSpread 结构的 region 宽度不足 `2 * maxHorizontalDistance`。

**Warning（可生成但可能不符预期）**

- 某输出 socket 的目标池与 fallback 池都没有兼容入口——该分支必然终止；
- processor 的 `chance` 为 0，永不执行；
- 风化处理器的 accent 与 primary 相同，视觉上无效果；
- 处理器向下深度会触到世界底部而被截断；
- 环候选数少于环数（外环空置）；
- 所有环都落在自身布局半径内（候选必然互相重叠）。

构造 `JigsawStructureFeatureSettings` 本身还会抛异常拦下更硬的错误：
piece ID 重复、connector ID 重复、start piece 不唯一、`firstPieceId`
不存在或在深度 1 不合法、`Σ minimumCount + 1 > maxPieces`、楼层高度
不足以容纳最高模块。

## 8. 测试覆盖

`Assets/Game/Tests/Editor/JigsawStructureGeneratorTests.cs`：

- 默认世界资产接线，矿洞与 fortress 的模块丰富度；
- 同种子布局完全一致（确定性）；
- minimum / maximum count 在多种子采样下的达成与不越界；
- 显式 socket、pool 跳转、图校验；
- library 书架、support frame、侧分支门洞、masonry 室内的实际体素；
- 模板 piece 旋转后写入自带调色板与空气；
- 模板 socket 被无 connector 的模块继承，且落点等于旋转后的标记位置；
- 模板 socket 变更会改变 ContentHash（缓存失效）；
- 四种处理器各自的行为边界（支柱遇地形停、地基不超厚、清顶不超高、
  风化确定且只碰本结构体素）；
- 处理器不影响碰撞决策；
- RandomSpread 的服务枚举与逐区域查询一致；
- 环候选确定、跨多个半径带、窗口查询正确剪枝；
- structure set 每格恰好一个胜者，且权重大者胜出更多；
- 无 set 的结构互不竞争；
- 布局缓存同键复用、并发只构建一次、容量有上限。

`VoxelTypeMarchingCubesTests.cs` 覆盖分组网格：同组连成一体、同组仍保留
每类型材质、跨组仍有内缩接缝、未登记类型不误并组。

## 9. 当前边界

以下能力尚未实现，本文与代码都不把它们描述为已完成：

- **Terrain matching**：无高度图投影，道路无法逐列贴合地形。当前结构均为
  地下（默认资产楼层高度 30~200），该能力价值有限。
- **Recursive grammar layout**：无末地城式"一条规则产生一批 piece、
  整组碰撞、失败整组回滚"的事务式布局器。当前每个 socket 独立选取单个
  piece，无法忠实表达"桥末必须接建筑，否则不留半截桥"。
- **Biome 过滤**：选址不检查生物群系。
- **Marker / loot pipeline**：无箱子、刷怪笼、矿车轨道、门、光源、实体
  等数据标记后处理。
- **Empty element**：模板池不支持原版"高权重空元素"来控制密度，当前用
  socket 的 `activationChance` 近似。

后续扩展应保持 placement / 缓存 / 空间索引 / rasterizer 基础设施共享，
再分别增加 Template Jigsaw 与 Recursive Grammar 策略，而不是把所有结构
语义继续塞进同一个随机队列。

## 10. 代码索引

| 文件 | 职责 |
|---|---|
| `Runtime/Structures/JigsawStructureFeatureDefinition.cs` | 结构族资产（可编辑） |
| `Runtime/Structures/JigsawStructureFeatureSettings.cs` | 结构族不可变快照 + ContentHash |
| `Runtime/Structures/JigsawPieceDefinition.cs` | 模块资产 |
| `Runtime/Structures/JigsawPieceSettings.cs` | 模块快照（含模板数据） |
| `Runtime/Structures/JigsawConnectorDefinition.cs` | socket 资产 |
| `Runtime/Structures/JigsawConnectorSettings.cs` | socket 快照 + 名称匹配 |
| `Runtime/Structures/JigsawProcessorDefinition.cs` | 处理器资产 + 快照 |
| `Runtime/Structures/JigsawPlacementService.cs` | 选址策略 + structure set 竞争 |
| `Runtime/Structures/JigsawStructureGenerator.cs` | 布局、缓存、裁剪、落地 |
| `Runtime/Structures/JigsawStructureValidator.cs` | 图与配置校验 |
| `Runtime/Voxels/VoxelStructureSocket.cs` | 模板内 socket 标记 |
| `Runtime/Voxels/VoxelGroup.cs` | 体素分组与网格连续性 |
| `Editor/WorldGeneration/JigsawStructureAssetBuilder.cs` | 默认矿洞 / fortress 重建 |
| `Editor/WorldGeneration/JigsawStructureFeatureDefinitionEditor.cs` | Inspector 与校验展示 |
