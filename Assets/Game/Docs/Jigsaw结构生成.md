# Jigsaw 结构生成算法

> 本文完整描述 Supernova 当前的 Jigsaw 结构生成实现：从选址、布局、落地到缓存与校验。
>
> 面向程序读者。若只想配置一个新结构而不改代码，请读同目录的 `Jigsaw结构配置手册.md`。
>
> 算法背景与 Minecraft 原版对照见 `Guideline/Minecraft生成式结构算法指南.md`。
>
> **未完成工作请直接看第 9 节**，那里逐项列出了缺口、影响、涉及文件与建议实现顺序。

## 0. 实现状态一览

| 能力 | 状态 | 章节 |
|---|---|---|
| RandomSpread 选址 | ✅ | §2.1 |
| ConcentricRings 选址 | ✅ | §2.2 |
| Structure set 加权竞争 | ✅ | §2.3 |
| Frontier piece graph 布局 | ✅ | §3.1 |
| 显式 socket 双向名称匹配 | ✅ | §3.3 |
| min/maxCount、配额、必需目标、整图重试 | ✅ | §3.2 §3.5 |
| Terminator fallback pool | ✅ | §3.2 |
| Spatial hash 碰撞 | ✅ | §3.4 |
| 布局缓存 + 柱裁剪索引 | ✅ | §4 §6 |
| Shell/Air/Accent/Processor 四遍落地 | ✅ | §5.1 |
| Voxel template piece（含旋转、保留调色板） | ✅ | §5.3 |
| 模板内 socket marker | ✅ | §5.3 |
| Processor：支柱/地基/清顶/风化 | ✅ | §5.4 |
| 宝藏 / 特殊位置 spawn marker | ✅ | §5.6 |
| **完整 Template Jigsaw（模板池、empty element）** | ❌ | §9.1 |
| **Terrain matching / 高度图投影** | ❌ | §9.2 |
| **Recursive Grammar（末地城式事务分支）** | ❌ | §9.3 |
| **Biome 过滤** | ❌ | §9.4 |
| **Loot / spawner / rail 等 marker processor** | ❌ | §9.5 |

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

朝向还必须可对齐：四个水平面之间可通过 yaw 旋转互配；通用 `Up` 只匹配
`Down`，`Down` 只匹配 `Up`。垂直连接不会把整个 piece 翻转或侧转，
子 piece 始终保持直立并继承父 piece 的 yaw，使局部方向定义的装饰和通道
保持一致。

对齐过程：

1. 枚举候选模块所有兼容的 input socket，用蓄水池抽样等概率取一个
   （`random.NextInt(++matchingCount) == 0`），保证与枚举顺序无关；
2. 水平 socket 由
   `direction = (Opposite(connector.Direction) - input.Face) & 3`
   定出子 piece 朝向；垂直 socket 已由 `Up↔Down` 保证法线相反，子 piece
   直接继承父 piece 的 yaw；
3. 按该朝向生成几何（Box / Passage / Template），得到临时 piece；
4. 计算子 input socket 在临时 piece 上的世界位置 `boundary`；
5. 整体平移 `connector.Position - boundary`，使两个 socket 严格贴合。

`GetAuthoredConnectorBoundary` 按模块类型分三种算法：

- **Template**：socket 自带模板局部坐标，直接绕 piece 朝向旋转该坐标。
  这是最精确的一种——标记知道自己贴在哪个体素上。
- **Box（Room / Crossing / VerticalShaft）**：水平 `face` 定出所在墙面，沿墙面法线取半跨，
  再叠加 `lateralOffset` 横移、`verticalOffset` 抬高；`Up` / `Down`
  分别落在顶板 / 底板，`alongOffset = -1` 时使用中心。
- **Passage（Corridor / Stairs）**：`Forward`/`Back` 取通道两端；
  `Right`/`Left` 取侧墙；`Up` / `Down` 取指定沿程处的顶 / 底面。
  沿通道位置由 `alongOffset` 决定（-1 表示中点）。

socket 激活只表示进入 frontier。只有子 piece 真正放置成功后，父输出门洞与
子输入门洞才会记入各自 piece 的 `Openings` 列表，因此 Masonry 外壳会在
两侧同时雕通；因碰撞、深度或数量限制失败的分支保持封闭。
带已连接 `Down` opening 的 piece 不再执行向下的 `FoundationFill` 或
`SupportToGround`，避免通用垂直孔洞被后执行的 processor 回填。水平 socket 的
`Opening Width / Height` 表示门洞宽高；垂直 socket 则表示水平孔洞的左右
宽度与前后长度。

当前 `NetherFortress` 不使用 `Up` / `Down` 楼板孔，也不使用楼梯。junction 的
Forward 墙面在横向 `+9` / `-9` 处分别提供概率 `0.12` / `0.08` 的 3×5 门洞，
连接 7×16×7 的空心电梯井式 corridor。井道输入与输出都在侧墙，高度分别为
`1` 与 `11`，因此另一端 9×7×9 landing 房间比来源房间高或低 10 个体素；
来源房间、井道底板与顶板、landing 楼板都保持完整。玩家只需从门口进入
竖直 corridor，具体升降方式不由 jigsaw 结构提供。

电梯链使用保留的 `fort_lift_*` socket 名称；DenseJigsaw 虽然会把普通水平
socket 改为通配连接，但 `fort_lift_*` 必须精确匹配，并保留方向、角色和
`ActivationChance`。井道只有一个概率为 1 的 landing 出口：生成器在放置井道
之前会预留一个 piece 数量和一个深度层级，并提前检查 landing 的边界、半径与
碰撞；该出口还会优先进入 frontier。只要 landing 无法落位，整个井道分支就会
被拒绝，junction 墙面也不会开门，从而保证已出现的入口一定连接到另一房间。

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

### 5.6 宝藏与特殊位置 marker

世界的自然散布对结构一无所知，因此"图书室台座上有战利品"这类设计意图
由 `StructureSpawnMarkerDefinition` 表达。怪物不再使用结构 marker 固定生成，
统一由世界级定时调度器在玩家当前区块 3 格之外随机生成。

**数据流**

```text
piece.spawnMarkers[]  (或 template.spawnMarkers[]，piece 未配置时继承)
  -> StructureSpawnMarkerSettings         不可变快照，携带 prefab 引用
  -> JigsawStructureGenerator.CollectSpawnRequests(column, ...)
       复用缓存布局，按世界坐标掷点
  -> StructureSpawnRequest[]              体素坐标 + yaw + 落地策略
  -> MinecraftCaveInfiniteWorld.SpawnStructureMarkers(column)
       主线程实例化
```

**关键设计**

- marker 的 `localOffset` 在 piece 自身坐标系内，随 piece 旋转，不会指向
  世界正北；
- 掷点键取 **marker 世界锚点**而非索引，因此任何柱、任意次访问都对同一
  marker 得出相同结论；
- `Count > 1` 时其余宝藏实例在 `scatterRadiusInVoxels` 内散开。散到邻柱的实例
  会被当前柱丢弃，交由那一柱自己解析，避免同一实例被生成两次；
- `snapToFloor` 向下最多找 `floorSearchDistance` 格，落在第一个"下方实体、
  自身为空"的位置。找不到则该实例不生成——宁可缺一个，也不要把宝箱塞进
  石头里；
- marker 参与 `ContentHash`，编辑后缓存自动失效。

自然怪物生成每 5 秒判定一次，默认以 0.3 概率从池中均匀随机一种怪物，
最多抽查 4 个已完成的远处区块，并且每次最多排队一只怪物。生成使用
`MonsterSpawnTable.playerExclusionRadiusInChunks`；候选位置和延迟队列真正
实例化时都会重新检查玩家当前位置，保证不会在 3 区块径向范围内新生成
怪物。使用外部 landing cell 的 DenseJigsaw 在玩家首次穿门进入内部前不会
启动该计时器。

实例化时机与自然生成一致：`FinalizeColumnPhysicsIfReady` 在该柱**全部**
mesh section 建好、碰撞体就绪之后调用，因此 `snapToFloor` 看到的是完工
地形。每柱只解析一次（`markerSpawnedColumns`）。

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
- 通用 `Up` / `Down` socket 的堆叠对齐与相反面匹配，以及 NetherFortress 电梯井的双门连通、完整楼板和 landing 原子落位；
- library 书架、support frame、侧分支门洞、masonry 室内的实际体素；
- 模板 piece 旋转后写入自带调色板与空气；
- 模板 socket 被无 connector 的模块继承，且落点等于旋转后的标记位置；
- 模板 socket 变更会改变 ContentHash（缓存失效）；
- 四种处理器各自的行为边界（支柱遇地形停、地基不超厚、清顶不超高、
  风化确定且只碰本结构体素）；
- 处理器不影响碰撞决策；
- 宝藏 marker 解析确定、随 piece 旋转、只由拥有它的柱上报、
  零概率不触发、编辑后 ContentHash 变化；
- 模板 marker 被无自身 marker 的模块继承；
- RandomSpread 的服务枚举与逐区域查询一致；
- 环候选确定、跨多个半径带、窗口查询正确剪枝；
- structure set 每格恰好一个胜者，且权重大者胜出更多；
- 无 set 的结构互不竞争；
- 布局缓存同键复用、并发只构建一次、容量有上限。

`VoxelTypeMarchingCubesTests.cs` 覆盖分组网格：同组连成一体、同组仍保留
每类型材质、跨组仍有内缩接缝、未登记类型不误并组。

## 9. 未完成工作

以下能力**尚未实现**。每项都写明缺什么、造成什么限制、要改哪些文件，
以便后续接手时能直接开工，不必重新调研。

建议顺序：9.1 → 9.5 → 9.4 → 9.2 → 9.3。前两项收益高、改动局部；
9.3 工作量最大且需要重构布局提交模型，应放到最后。

### 9.1 完整 Template Jigsaw（模板池与 empty element）

**现状**：模块与模板是一对一的。`voxelTemplate` 是单个字段，一个模块只能
长成一种样子。想要"5 种不同的农舍随机出一种"，必须建 5 个模块并让它们
共用同一个 pool 和同一组 socket 名字。

**缺什么**

- pool 内的**加权模板列表**：一个模块持有 N 个模板 + 权重，选中模块后再
  抽模板。原版 template pool 的 `elements[]` 即此。
- **empty element**：原版村庄在房屋池里放高权重空元素来控制密度。当前只能
  用 socket 的 `activationChance` 近似，但那控制的是"出口是否生长"，无法
  表达"这个插口被占用了，但放的是空气"。
- **list element**：一次放置多个模板（如房屋 + 门前小路）。

**影响的文件**

- `JigsawPieceDefinition.cs`：`voxelTemplate` 改为带权重的列表；
- `JigsawPieceSettings.cs`：模板数据数组化，`GetTemplateSample` 需要模板
  索引参数；`TemplateContentHash` 覆盖全部模板；
- `JigsawStructureGenerator.cs`：`TryCreateCandidate` 在选中模块后再抽模板，
  抽中的索引必须存进 `Piece`（新字段），否则落地时不知道用哪个模板；
- `Piece` 结构体加 `TemplateIndex`，`LayoutCacheEntry` 随之变化。

**注意**：模板索引必须进入 `Piece` 并参与布局缓存，否则同一 region 重放
时可能抽到不同模板，破坏 §1.1 的确定性契约。

### 9.2 Terrain Matching 与高度图投影

**现状**：所有 piece 使用刚性（rigid）投影——整块保持形状，地板一个统一 Y。
`Stairs` 是唯一例外，沿通道线性插值。

**缺什么**

- 世界生成阶段暴露**地表高度图**采样。当前 `GenerateColumn` 只拿到
  `densities[]`，没有"这一列地表在哪"的廉价查询；
- `terrainAdaptation` 字段（`Rigid` / `TerrainMatching` / `BeardThin`）；
- 逐列落地高度：`Piece` 目前只有 `StartFloorY` / `EndFloorY`，无法表达
  "道路每一列各自贴合地形"。需要 piece 携带逐列高度数组，或在落地阶段
  按列查询高度图。

**限制**：地表道路会悬空或硬切进山体。当前所有默认结构都在地下
（楼层 30~200），所以优先级不高。**若要做地表村庄，这是前置条件。**

**影响的文件**：`MinecraftCaveInfiniteWorld.GenerateChunkData`（传入高度图
采样器）、`JigsawStructureGenerator`（落地阶段逐列取高）、`Piece`。

### 9.3 Recursive Grammar 布局器（末地城式）

**现状**：布局是逐 socket 独立选取**单个** piece 的 FIFO frontier。每个
piece 一旦通过碰撞检查就立即提交，无法回滚。

**缺什么**

- **一条规则产生一批 piece**：`BUILDING` / `SMALL_TOWER` / `BRIDGE_PIECE`
  / `FAT_TOWER` 这类非终结符，一次生成整组几何；
- **事务式碰撞**：整组先在临时占用表中构造，与全局碰撞检查后**原子提交**，
  任一非法则整组回滚；
- **structure 级状态**：如"全城至多一艘船"的 `ShipGenerated` 标记；
- **深度相关概率**：递归越深，某些分支概率越低。

**限制**：无法表达"一段桥的末端必须接上建筑，否则不留下半截桥"。当前若
桥末的建筑放不下，那段桥会留在原地成为断桥。

**影响的文件**

- `JigsawStructureGenerator.cs`：`BuildLayoutAttempt` 需要重构——把"提交
  piece"从直接 `pieces.Add` + `spatialIndex.Add` 改为可回滚的事务；
  `PieceSpatialIndex` 需要支持 `BeginTransaction` / `Commit` / `Rollback`；
- 新增 `JigsawGrammarRuleDefinition`（规则资产）与
  `RecursiveGrammarLayoutStrategy`；
- `JigsawStructureFeatureDefinition` 加 `layoutStrategy` 字段，在
  `PieceGraph` 与 `RecursiveGrammar` 间选择。

**务必保留共享**：placement、缓存、空间索引、柱裁剪、rasterizer、processor
都应被新策略复用。不要把两种布局语义压回同一个随机队列——这正是
`Guideline/Minecraft生成式结构算法指南.md` §12 的结论。

### 9.4 Biome 过滤

**现状**：选址完全不看生物群系，`CaveBiomeCatalog` 与结构生成互不相识。

**缺什么**：结构资产上的 biome tag 白名单，以及选址阶段的准入检查。

**影响的文件**：`JigsawStructureFeatureDefinition`（加 biome 过滤字段）、
`JigsawPlacementService.CollectPlacements`（候选点处查询 biome）。
需要确认 biome 查询在 worker 线程安全且不依赖流送状态。

### 9.5 Loot / spawner / rail 等 marker processor

**现状**：`StructureSpawnMarkerDefinition` 已支持宝藏、检查点与玩家出生点
（§5.6），怪物统一走世界级随机生成。

**缺什么**

- **箱子 + 战利品表**：marker 指定一个 loot table，生成带随机内容的容器；
- **刷怪笼**：若未来需要，应作为独立机制持续生成，而不是恢复固定怪物 marker；
- **矿车轨道**：沿走廊铺设，需要感知 piece 朝向与长度；
- **门 / 光源 / 装饰实体**：批量小物件，可能需要与 `CaveSurfaceBrush`
  的实例化渲染合流以避免大量 GameObject。

**影响的文件**：`StructureSpawnMarkerDefinition`（扩展 `Kind` 枚举 +
对应字段）、`MinecraftCaveInfiniteWorld.SpawnStructureMarker`（新分支）。
§5.6 的数据流可以直接复用，这是当前最容易扩展的一项。

### 9.6 其他已知边界

- `minimumCount` 在空间完全不可行时返回"缺失最少"的布局，而不是突破碰撞
  强塞模块。这应由资产校验与多种子批量测试提前发现（见 §7、§8）。
- 处理器只有四种，且都是逐列垂直操作。没有水平方向的形态处理（如"沿墙
  加装饰带"）。
- marker 不支持"整组同时生成或都不生成"的事务语义；需要成组放置的非怪物
  内容应增加单独的事务机制。

## 10. 现有结构内容

七类结构，各自使用不同的调色板、形态与遭遇战定位：

| 结构 | 调色板（primary/accent） | 形态范式 | 特点 |
|---|---|---|---|
| `abandoned_mineshaft` | Stone / Dirt | 废弃矿洞 | Excavated 隧道网络，木支撑，骷髅巢穴 |
| `stronghold` | Marble / Bricks | 末地要塞 | **ConcentricRings** 选址；必达传送门房间 |
| `nether_fortress` | RustyMetal / WornBrick | 下界要塞 | **双池** bridge→corridor；junction 以低概率墙面门洞接入空心电梯井式 corridor，再连接 vertical landing；楼板完整、无楼梯 |
| `ancient_city` | TigerRock / RustyMetal | 远古城市 | 手绘十字神殿为起点，低矮长厅 |
| `cave_village` | WornBrick / TigerRock | 村庄 | **道路优先**：房屋挂在道路侧插口上 |
| `ancient_prison` | WornBrick / RustyMetal | 监牢 | 小尺寸封闭空间 |
| `cactus_grotto` | Dirt / Stone | 天然巢穴 | 全 Excavated，无砌造，仙人掌群 |

`nether_fortress` 与 `ancient_city` 共享 structure set `deep_complexes`
（权重 3:2），同一候选格只出其一。

四个手绘模板作为 jigsaw piece 载入，携带各自的 socket，部分模板还有宝藏 marker：

| 模板 | 尺寸 | 用于 | 自带 |
|---|---|---|---|
| `AncientCityShrine` | 19×13×19 | ancient_city 起点 | 4 socket |
| `VillageHouse` | 11×10×13 | cave_village 房屋 | 1 socket + 宝藏 marker |
| `VillageWell` | 13×8×13 | cave_village 起点 | 4 socket |
| `GrottoNest` | 17×12×17 | cactus_grotto 起点 | 2 socket |

### 10.1 体素类型

| 类型 | ID | Group | 材质 |
|---|---|---|---|
| Default | 1 | Structure | （无） |
| Stone | 2 | Stone | Stone |
| Ore | 3 | Ore | Ore |
| Bedrock | 4 | Stone | Bedrock |
| StructureBrick | 5 | Structure | Marble |
| FortressBrick | 6 | Structure | Bricks |
| Dirt | 7 | Stone | Dirt |
| RustyMetal | 8 | Structure | RustyMetal |
| TigerRock | 9 | Structure | TigerRock/Bricks |
| WornBrick | 10 | Structure | WornBrick |

⚠️ 一个结构的 primary 与 accent **必须同组**，否则 Marching Cubes 会把两者
当成两个曲面并内缩分界，结构墙面出现坑洼。
`AuthoredStructures_KeepPrimaryAndAccentInOneVoxelGroup` 测试守住这一点。

## 11. 代码索引
| 文件 | 职责 |
|---|---|
| `Runtime/Structures/JigsawStructureFeatureDefinition.cs` | 结构族资产（可编辑） |
| `Runtime/Structures/JigsawStructureFeatureSettings.cs` | 结构族不可变快照 + ContentHash |
| `Runtime/Structures/JigsawPieceDefinition.cs` | 模块资产 |
| `Runtime/Structures/JigsawPieceSettings.cs` | 模块快照（含模板数据） |
| `Runtime/Structures/JigsawConnectorDefinition.cs` | socket 资产 |
| `Runtime/Structures/JigsawConnectorSettings.cs` | socket 快照 + 名称匹配 |
| `Runtime/Structures/JigsawProcessorDefinition.cs` | 处理器资产 + 快照 |
| `Runtime/Structures/StructureSpawnMarkerDefinition.cs` | 宝藏 / 检查点 / 玩家位置 marker 资产、快照与解析结果 |
| `Runtime/Structures/JigsawPlacementService.cs` | 选址策略 + structure set 竞争 |
| `Runtime/Structures/JigsawStructureGenerator.cs` | 布局、缓存、裁剪、落地、marker 解析 |
| `Runtime/Structures/JigsawStructureValidator.cs` | 图与配置校验 |
| `Runtime/Voxels/VoxelStructureSocket.cs` | 模板内 socket 标记 |
| `Runtime/Voxels/VoxelStructureAsset.cs` | 体素模板，含 socket 与 marker |
| `Runtime/Voxels/VoxelGroup.cs` | 体素分组与网格连续性 |
| `Runtime/Creatures/MonsterSpawnTable.cs` | 怪物池、定时概率、候选数量、总上限与玩家排除半径 |
| `Runtime/MinecraftCaveInfiniteWorld.cs` | 世界流送、marker 实例化与自然怪物生成 |
| `Editor/WorldGeneration/JigsawStructureFeatureDefinitionEditor.cs` | Inspector 与校验展示 |

结构资产是**唯一**定义来源。项目里不存在用代码重建这些资产的 builder
脚本——那类脚本会产生第二份定义并与资产漂移，已被删除。
