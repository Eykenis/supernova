# Minecraft 生成式结构生成算法指南

> 调研对象：Java Edition 现代世界生成体系，重点分析末地要塞（Stronghold）、村庄（Village）、下界要塞（Nether Fortress）、末地城（End City），并映射到 Supernova 当前结构生成架构。
>
> 结论基于 Minecraft Wiki、Java Edition 世界生成数据、Yarn 映射以及可读的 1.21.x Java 实现交叉验证。具体常量可能随版本调整，但四类布局算法的基本范式长期稳定。

## 1. 核心结论

Minecraft 的“生成式结构”不是一套算法，而是四层流水线与多种布局器的组合：

1. **全局选址（Placement）**：决定哪些区块成为候选起点；
2. **环境准入（Structure validation）**：检查维度、生物群系、地表高度、岛屿面积等；
3. **结构布局（Layout）**：生成模块图、方向、包围盒和父子关系；
4. **模板落地（Placement/Processors）**：将模板或程序化 piece 写入区块，并进行地形适配、方块替换、实体与战利品标记处理。

四个重点结构对应三种主要布局范式：

| 结构 | 全局选址 | 局部布局范式 | 模块表达 |
|---|---|---|---|
| 末地要塞 | 同心环 | 加权 piece graph / frontier expansion | 硬编码程序化 piece |
| 村庄 | 网格随机散布 | 通用 Jigsaw socket expansion | NBT 模板 + template pool |
| 下界要塞 | 网格随机散布，与堡垒遗迹共享结构集竞争 | 双池加权 piece graph | 硬编码程序化 piece |
| 末地城 | 网格随机散布 + 岛屿高度准入 | 深度受限递归生成语法 | NBT 模板 + 专用递归策略 |

因此，若目标是获得 Minecraft 风格而不是只获得“随机房间”，推荐保留统一的选址、确定性随机、碰撞和分区回放框架，同时允许结构选择不同的 **Layout Strategy**，不要强制所有结构共用同一种 Jigsaw 图扩展规则。

## 2. 通用四阶段模型

### 2.1 全局选址

结构起点不是逐区块独立掷骰子。Minecraft 主要使用：

- **Random Spread**：将世界划分为 `spacing × spacing` 区块的区域，每个区域选择一个候选区块；`separation` 保证候选点不会贴近区域边缘，从而控制结构间最小间隔。偏移可用均匀或三角分布。
- **Concentric Rings**：按世界原点生成多个同心环，每环内按近似等角度放置结构，并可向偏好的生物群系修正。

现代 Java 世界生成数据中的典型参数：

- Stronghold：`concentric_rings`，`count=128`、`distance=32`、`spread=3`；
- End City：`random_spread`，`spacing=20`、`separation=11`、三角分布；
- Nether complexes：`random_spread`，`spacing=27`、`separation=4`，同一候选点在下界要塞与堡垒遗迹之间按权重竞争。

村庄也由各村庄 structure set 的 Random Spread 决定候选起点，之后才进入 Jigsaw 布局。

### 2.2 环境准入

候选起点还需通过环境检查：

- 起点生物群系是否属于结构允许的 biome tag；
- 地表结构是否有合适的高度和地形；
- 地下结构是否允许埋入地形；
- 末地城所在区块附近是否存在足够高、足够大的外岛地形；
- 同一 structure set 内多个结构按权重只选择一个。

选址与布局应分离。这样 `/locate`、流送、预览和实际生成都可以共享同一确定性候选计算。

### 2.3 布局

布局阶段只生成轻量数据：

- piece/template ID；
- 世界原点、旋转、镜像；
- 包围盒或体素占用形状；
- 父节点、深度、连接口；
- 特殊状态，例如末地城是否已经生成船、要塞是否已生成传送门房间。

Minecraft 的重要共性是：**先求布局和碰撞，再按区块裁剪落地**。这使跨区块结构不依赖区块生成顺序。

### 2.4 模板落地与 Processor

布局成功后才写方块。模板落地通常包含：

- 旋转、镜像与局部坐标变换；
- 忽略结构方块、选择是否忽略空气；
- `rigid` 或 `terrain_matching` 投影；
- 腐化、苔藓化、随机替换等 processor；
- 连接地面的支柱、清除上方空间；
- 数据标记驱动的箱子、刷怪笼、村民、潜影贝、物品展示框等后处理。

## 3. 末地要塞 Stronghold

### 3.1 全局分布：同心环

Java Edition 固定最多生成 128 个末地要塞。结构按多层同心环围绕世界原点分布，同一环中的角度近似均匀，角度和半径带有种子随机扰动，并可向偏好生物群系调整。

其设计目的不是自然聚类，而是：

- 保证从世界中心向外探索时具有可预期的发现密度；
- 避免全部结构落在纯随机位置造成极端空洞；
- 通过不同环逐渐扩大覆盖范围。

### 3.2 局部布局：加权 piece graph

Stronghold 不是现代通用 Jigsaw。其布局由硬编码 piece 类扩展：

1. 建立起始螺旋楼梯 `Start`，随机选择水平方向；
2. piece 暴露前、左、右等出口；
3. 从待扩展出口继续创建子 piece；
4. 按权重选择候选类型；
5. 校验深度、类型数量上限、特殊房间最小深度、包围盒碰撞；
6. 成功则加入结构图并将新出口加入 frontier；
7. 多次失败时尝试短小封口走廊，否则该分支终止。

典型模块包括普通走廊、左右转弯、监牢、方形房间、楼梯、螺旋楼梯、五向路口、宝箱走廊、图书馆和传送门房间。

### 3.3 加权、配额与语义约束

原版 piece 表同时拥有：

- `weight`：随机抽取权重；
- `limit`：单座结构最大出现次数，0 表示无硬上限；
- `generatedCount`：当前已生成次数；
- `canGenerate(depth)`：深度约束；
- `lastPiece`：避免连续重复同一模块。

例如图书馆要求链深大于 4，传送门房间要求链深大于 5，且传送门房间最多一个。选择过程会重试若干次，并拒绝与已有 piece 包围盒相交的候选。

现代实现还限制最大链深和相对起点的水平范围。其关键不是精确数值，而是同时存在：

- 图深限制；
- 半径限制；
- 类型配额；
- 最小出现深度；
- 特殊目标房间唯一性。

### 3.4 必达目标的处理

传送门房间是功能目标，但单次随机布局未必成功。原版 Stronghold 的更外层构建流程会检查是否产生传送门房间；失败时会用变化后的随机状态重新构造，直到得到有效结构。

这是值得复用的模式：

```text
for attempt in 0..MaxLayoutRetries:
    layout = Build(seed, attempt)
    if layout satisfies required objectives:
        return layout
return deterministic fallback layout
```

比在图扩展末尾强行把目标房间塞入一个不合适的位置更稳定。

### 3.5 地形与材质

Stronghold 的 piece 是程序化几何而非 NBT 模板。每个 piece 自己生成墙、门、台阶、装饰和特殊方块。石砖使用随机调色规则混合普通、裂纹、苔石砖等。结构整体采用埋入式地形适配，洞穴或峡谷可能切穿结构，这是世界生成阶段交互的结果。

### 3.6 可抽象能力

若在本项目中实现 Stronghold，应在现有 piece 定义上增加：

- `MaxCount` / `MinDepth` / `MaxDepth`；
- `MayRepeatImmediately`；
- `RequiredObjective`；
- 每个 piece 的显式 socket 集，而不是只从 Shape 推导 connector；
- 布局重试与有效性验证；
- 封口 piece/fallback pool。

## 4. 村庄 Village

### 4.1 现代村庄使用通用 Jigsaw

村庄是最典型的数据驱动 Jigsaw 结构。以平原村庄为例，结构定义包含：

- `start_pool = village/plains/town_centers`；
- `size = 6`，控制最大拼接深度；
- 起点投影到 `WORLD_SURFACE_WG` 高度图；
- `max_distance_from_center = 80`；
- `terrain_adaptation = beard_thin`；
- 启用 expansion hack，降低大房屋因占用空间不足而被过早截断的概率。

### 4.2 Jigsaw socket 匹配

每个模板内包含 Jigsaw connector。连接器至少表达：

- 自身 `name`；
- 要匹配的 `target`；
- 下一模块来自哪个 `pool`；
- 连接器朝向与 joint 类型；
- 拼接完成后替换成的最终方块状态。

布局器从当前 piece 的连接器读取目标池，打乱/加权候选模板与旋转，寻找朝向相反且 name/target 兼容的 connector，然后对齐两个 connector 的世界坐标。

近似流程：

```text
queue <- start piece connectors
while queue not empty:
    parentConnector <- pop queue
    candidates <- shuffled weighted elements(targetPool)
    for candidate in candidates:
        for rotation in rotations:
            for childConnector in candidate.connectors:
                if sockets match:
                    transform child so sockets become adjacent
                    adjust Y according to projection/joint
                    if child shape fits free space and bounds:
                        accept child
                        enqueue child connectors
                        continue outer loop
    try fallback pool / terminator
```

### 4.3 Template Pool

池中元素具有权重，并可包含：

- 单模板元素；
- legacy single 模板元素；
- list element；
- feature element；
- empty element。

村庄把道路、房屋、装饰和终止件拆到不同池。例如平原道路池包含直路、转弯和十字路口，并以 terminator 池作为 fallback；房屋池中还显式加入高权重 empty element，使部分房屋插口自然留空，从而控制密度。

### 4.4 道路优先形成骨架

村庄并不是在平面上直接随机散布房屋。Town center 首先连出道路模块；道路模板又暴露：

- 继续道路的 socket；
- 向两侧挂接房屋/农田/装饰的 socket。

于是道路网络成为空间骨架，建筑依附于道路。这样天然保证入口朝路、建筑之间保持由模板预留的间距。

### 4.5 地形适配

村庄最难的部分不是拼接，而是地形：

- 道路通常使用 `terrain_matching`，模板内各列可随高度图投影；
- 房屋通常使用 `rigid`，保持整体不变形；
- processor 负责道路替换、地基向下填充、上方清理、苔藓化等；
- 整体 `beard_thin` 会在地形密度阶段对结构附近地形做平滑支撑，降低悬空和硬切边。

因此村庄需要区分 **布局坐标** 和 **最终每列落地高度**。只在 piece 中保存一个统一 Y，无法正确表现山地道路。

### 4.6 可抽象能力

本项目当前 `poolId/outputPoolId` 已具备池跳转雏形，但要达到村庄级别还需要：

- 每个模块多个可编辑 socket；
- socket 的 name/target/pool/orientation/joint；
- 模板资产而不仅是参数化盒体；
- `Rigid` / `TerrainMatching` 两种投影；
- fallback/terminator pool；
- empty element；
- processor pipeline；
- 地表高度采样和地基生成。

## 5. 下界要塞 Nether Fortress

### 5.1 选址：与堡垒遗迹竞争

现代 Java 数据把下界要塞与堡垒遗迹放进同一个 `nether_complexes` structure set。候选点使用 Random Spread；在候选点上按权重选择结构，下界要塞权重 2、堡垒遗迹权重 3。

这说明“区域是否有结构”和“区域生成哪种结构”是两个不同问题。项目若以后加入同类竞争结构，宜在 Placement 层支持 weighted structure set，而不是让每个 feature 独立掷骰子造成重叠。

### 5.2 局部布局：双模块池

Nether Fortress 与 Stronghold 类似，使用硬编码加权 piece graph，但有两个语义池：

- **Bridge pool**：大型开放桥梁、交叉口、楼梯、平台等；
- **Corridor pool**：较封闭的走廊、转角、路口、阳台、下界疣房间等。

Start piece 保存两套剩余候选列表和统一的待扩展 piece 列表。父 piece 的某个出口在生成子节点时会指定使用 bridge pool 还是 corridor pool，因此结构常从开阔桥区逐渐进入封闭走廊区。

### 5.3 加权和重复控制

每种 PieceData 包含：

- 类型；
- 权重；
- 最大数量；
- 是否允许连续重复；
- 已生成数量。

生成器按总权重选择模块，拒绝超额模块或不允许的连续重复。候选必须通过：

- 最低 Y/世界边界检查；
- 最大链深/离起点距离；
- 旋转后包围盒不碰撞。

无法继续时用 BridgeEnd 等封口件结束分支。

### 5.4 支柱与地形相交

下界要塞 piece 的底层常向下填充到遇到实体地形，使桥和平台具有连续支柱。生成器并不要求整个结构预先落在平坦空间中，而是允许嵌入下界地形、跨越洞穴和熔岩海。

这类支撑不是布局阶段碰撞盒的一部分，而是落地 processor/后处理。若把无限向下支柱纳入布局包围盒，会造成大量不必要的碰撞拒绝。

### 5.5 可抽象能力

适合本项目的抽象不是“新增一个 FortressGenerator”，而是给通用 piece graph 增加：

- 多个命名 pool；
- socket 指定 child pool；
- 每池独立权重与配额；
- 连续重复策略；
- terminator；
- `SupportToGround` 落地处理；
- 特殊标记处理（箱子、刷怪点、农作物区）。

当前项目的 Fortress 资产已经采用 Masonry + pool 思路，但仍缺少原版双池状态、类型配额和显式封口，因此生成形态更接近通用地下房间图，而非原版下界要塞。

## 6. 末地城 End City

### 6.1 选址和准入

末地城使用 Random Spread，现代数据为 `spacing=20`、`separation=11`、三角偏移。候选区块还要通过末地外岛地形与高度检查，因此空岛、过低地形或不允许的 biome 不会生成城市。

### 6.2 不是通用 Jigsaw，而是递归生成语法

End City 使用预制模板，但模板组合逻辑由专用策略硬编码。核心非终结符可以理解为：

- `BUILDING`：生成不同高度的房间组合；
- `SMALL_TOWER`：生成若干塔身，并尝试从塔身四面分叉桥；
- `BRIDGE_PIECE`：生成直桥或两种楼梯桥，末端连接新建筑或船；
- `FAT_TOWER`：生成大型塔，并从若干层向外生成桥。

这更接近随机上下文无关图语法，而不是“所有 socket 从一个池统一抽取”。每个策略知道要放哪些模板、相对偏移、下一步调用哪个策略。

### 6.3 递归深度和事务式碰撞

总体最大递归深度为 8。每次递归分支先在临时列表中创建整组 piece：

1. 为这一分支生成一组模板；
2. 给同组 piece 标记相同 branch/chain ID；
3. 检查该组与全局已有结构的包围盒碰撞；
4. 同组内部和允许的父连接不视为非法；
5. 任一非法碰撞则整组回滚；
6. 全部合法才一次性提交。

这是末地城比简单 BFS 更重要的特征：**分支是事务**。例如一段桥最终必须能接入建筑；若末端建筑碰撞，则不能只留下半截无意义桥。

### 6.4 塔、桥与船

典型生成过程：

- 固定创建底部数层和屋顶；
- 从屋顶生成小塔；
- 小塔高度随机，某个中间塔节可成为桥接层；
- 每个候选方向独立决定是否尝试桥；
- 桥由 1～4 个段组成，每段可为直桥、陡梯或缓梯；
- 桥末端通常递归生成新建筑；
- 船由结构级布尔状态保证至多一艘，生成概率还受当前递归深度影响；
- 若小塔没有合适的桥接层，可能转入胖塔策略。

Wiki 常将“每个方向 50% 桥、桥有 12.5% 船”作为玩家可见概括；源码层面实际概率由多个随机分支与深度条件共同决定，且船一旦生成后整座城市禁止再次生成。

### 6.5 模板拼接方式

End City 模板不是通过通用 Jigsaw block 寻找配对，而是代码提供：

- 父模板；
- 相对连接偏移；
- 子模板名；
- 旋转；
- 是否忽略空气。

生成器通过父、子模板坐标变换计算子模板世界平移。因此它的连接点实际存在于代码表，而不是模板内可发现的 socket 元数据。

### 6.6 可抽象能力

若项目需要末地城式强轮廓结构，应增加 `Grammar Layout Strategy`：

- Strategy/Rule 可递归调用其他 Rule；
- Rule 一次产生多个 piece；
- 分支临时构造、整体碰撞验证、原子提交；
- 结构级状态变量，如 `ShipGenerated`；
- 深度相关概率；
- 模板连接锚点与旋转变换。

仅靠当前“逐 connector 独立选一个 piece”的队列无法忠实表达桥末建筑回滚、全局唯一船和 SMALL_TOWER/FAT_TOWER 状态转换。

## 7. 四类算法对比

| 维度 | Stronghold | Village | Nether Fortress | End City |
|---|---|---|---|---|
| 布局驱动 | 专用代码 | 数据驱动 Jigsaw | 专用代码 | 专用递归语法 |
| 扩展容器 | frontier/pending pieces | connector queue | pending pieces | 递归调用栈 |
| 选择方式 | 单池加权 + 配额 | connector 指向 pool，加权模板 | bridge/corridor 双池 + 配额 | 规则内部概率分支 |
| 连接定义 | piece 方法 | 模板内 Jigsaw socket | piece 方法 | 代码中的模板锚点 |
| 碰撞 | 单 piece AABB | 体积形状/AABB 空间占用 | 单 piece AABB | 整个递归分支事务检查 |
| 地形适配 | 埋入 | 道路 terrain matching，房屋 rigid | 支柱向下、允许穿插地形 | 起点地表准入，主体 rigid |
| 必达目标 | Portal Room | 无单一必达房 | 无 | 船可选且全局唯一 |
| 终止方式 | 小走廊/无候选 | fallback/terminator/empty | BridgeEnd | 深度、概率、碰撞回滚 |

## 8. 对 Supernova 当前实现的评估

当前项目已有能力：

- 按 region 和 seed 确定性选址；
- `maxPieces`、`maxDepth`、`maxHorizontalDistance`；
- piece pool 和权重；
- connector queue；
- AABB 碰撞拒绝；
- 按体素柱重放布局，独立于流送顺序；
- Masonry 分 Shell/Air/Accent 多 pass 写入，避免接缝被封；
- ScriptableObject 数据驱动配置。

这些已经覆盖了 Minecraft 结构系统最重要的工程骨架。但当前实现把模块限制为 Room/Corridor/Crossing/Stairs 参数组合，连接器也主要由 Shape 与 ConnectorPattern 推导，因此表达能力处在“简化的 piece graph”阶段。

### 8.1 当前最接近的原版结构

- 当前矿洞与 Stronghold/Nether Fortress 的 frontier expansion 思路相近；
- 当前 pool/outputPool 已接近下界要塞双池与 Jigsaw target pool；
- 当前还不能准确表达村庄的模板 socket 和 terrain matching；
- 当前不能表达末地城的事务式递归分支。

### 8.2 推荐架构

建议把 `JigsawStructureGenerator` 中可共享部分提取为三层：

```text
StructurePlacementService
  - RandomSpreadPlacement
  - ConcentricRingsPlacement
  - WeightedStructureSet

StructureLayoutService
  - PieceGraphLayoutStrategy       // Stronghold、Nether Fortress、矿洞
  - JigsawPoolLayoutStrategy       // Village、Trial Chamber 等
  - RecursiveGrammarLayoutStrategy // End City 风格

StructureRasterizer
  - ProceduralPieceRasterizer
  - VoxelTemplateRasterizer
  - ProcessorPipeline
```

三种布局器共享：

- DeterministicRandom 与 seed derivation；
- `StructurePlacement` / `StructurePiecePlacement`；
- 旋转后 bounds；
- 空间索引和碰撞；
- 世界柱裁剪；
- layout cache/replay；
- 调试可视化与测试工具。

### 8.3 数据模型增量

建议逐步增加：

```text
StructureFeatureDefinition
  placementStrategy
  layoutStrategy
  requiredObjectives[]
  layoutRetryCount
  terrainAdaptation

PieceDefinition
  template / proceduralShape
  weight
  minDepth / maxDepth
  maxCount
  repeatPolicy
  sockets[]
  processors[]

SocketDefinition
  localPosition
  forward / up
  name
  target
  targetPool
  joint

PoolDefinition
  elements[]
  fallbackPool
```

### 8.4 碰撞数据结构

Minecraft 规模不大时线性扫描 AABB 已可用，但本项目无限流送中同一布局可能被多柱重建。推荐：

- 布局阶段使用整数 AABB；
- 大型/不规则模板可增加体素占用 shape，减少 AABB 过度拒绝；
- accepted piece 放入 2D/3D spatial hash，按结构 cell 查询；
- 对末地城式分支使用临时 occupancy transaction；
- 缓存 `(worldSeed, featureStableId, region)` 对应布局，避免每柱重复构图。

缓存是优化而不是正确性前提；布局仍必须能够由 seed 完全重放。

## 9. 推荐实施顺序

### 阶段 A：强化现有 Piece Graph

优先支持 Stronghold/Nether Fortress 风格：

1. Piece 显式 sockets；
2. 多 pool；
3. `minDepth/maxDepth/maxCount/repeatPolicy`；
4. fallback terminator；
5. required objective + deterministic retry；
6. support-to-ground processor。

这一阶段改动最小，且可以直接增强现有 Fortress。

### 阶段 B：Voxel Template + Processor

1. 将 `VoxelStructureAsset` 扩展为可旋转模板模块；
2. 在模板中保存 socket marker；
3. 支持空气覆盖模式；
4. processor 链：替换、随机老化、地基、清顶、战利品/刷怪标记；
5. 模板和配置资产继续通过现有全局路径表 `ProjectAssetPaths` 管理编辑器创建路径，运行时通过资产引用而非硬编码路径加载。

### 阶段 C：完整 Jigsaw

1. Socket name/target/pool 匹配；
2. weighted pool + empty element；
3. fallback/terminator；
4. Rigid/TerrainMatching；
5. 地表高度图与道路投影；
6. Village 原型验证。

### 阶段 D：递归语法

1. Rule 资产或代码策略；
2. 一条规则产生 piece batch；
3. transaction collision；
4. structure-level flags；
5. End City 风格塔—桥—建筑原型。

## 10. 测试建议

除现有确定性与体素写入测试外，建议增加：

- 相同 seed/region 的布局序列化结果完全一致；
- 不同柱生成顺序的最终体素完全一致；
- weighted piece 的配额绝不超限；
- required objective 在允许重试时必定存在；
- terminator 不产生开放悬空 socket；
- 不同 pool 的选择不会串池；
- rigid piece 不随高度图变形；
- terrain-matching 道路逐列贴合高度；
- 事务分支碰撞时不留下半截桥；
- structure-level unique piece 最多一个；
- 支柱只影响布局 bounds 之外的落地 pass，不改变碰撞决策；
- structure set 内竞争结果确定且只选择一个结构。

## 11. 资料来源

### 世界生成数据

- Misode vanilla-worldgen 镜像：`worldgen/structure_set/strongholds.json`
- Misode vanilla-worldgen 镜像：`worldgen/structure_set/end_cities.json`
- Misode vanilla-worldgen 镜像：`worldgen/structure_set/nether_complexes.json`
- Misode vanilla-worldgen 镜像：`worldgen/structure/village_plains.json`
- Misode vanilla-worldgen 镜像：`worldgen/template_pool/village/plains/*`

### 实现与映射

- Yarn mappings：`StrongholdGenerator`、`NetherFortressGenerator`、`StructurePoolBasedGenerator`
- Java 1.21.x 可读实现：`StrongholdGenerator.java`、`EndCityGenerator.java`

### Wiki

- Minecraft Wiki: Stronghold
- Minecraft Wiki: Village
- Minecraft Wiki: Jigsaw structure / Template pool / Processor list
- Minecraft Wiki: Nether Fortress / Structure
- Minecraft Wiki: End City / Structure
- Minecraft Wiki: Structure set

## 12. 一句话设计准则

**复用确定性选址、空间碰撞、分区落地和模板处理；不要把 Stronghold、Village、Nether Fortress、End City 的布局语义强行压成同一种随机队列。**

## 13. 2026-08 实施状态

阶段 A“强化现有 Piece Graph”已经落地：

- piece 支持显式、可旋转 socket，以及 name/target/pool/fallback/开口尺寸匹配；
- 支持 minimum/maximum count、连续重复规则、必需目标优先和确定性整图重试；
- 支持 terminator fallback pool；
- 碰撞从全表线性扫描升级为 spatial hash；
- 按 feature 内容哈希、世界种子和 region 缓存布局，并预建区块相交索引；
- 默认矿洞和 fortress 已迁移到显式 socket，fortress 包含唯一 portal room、library、prison、stairs、crossing 和封口模块；
- Inspector 会检查无出口起点、空目标池、不兼容 socket 和不可达必需模块。

阶段 B～D 仍是后续独立工作：Voxel Template + Processor、完整 Template Jigsaw、Terrain Matching、Placement Strategy 和 Recursive Grammar。当前代码与文档不会把这些尚未实现的能力描述为已完成。

