# 拼接结构（Jigsaw Structure）生成与编辑

## 1. 当前架构

世界配置只保存 `JigsawStructureFeatureDefinition` 列表，不为矿洞、堡垒等内容增加专用顶级类型。
一类结构由一个结构资产和若干 `JigsawPieceDefinition` 模块组成：

```text
MinecraftWorldGenerationConfiguration.jigsawStructures
  -> JigsawStructureFeatureDefinition
       -> placement / layout policy / materials
       -> JigsawPieceDefinition[]
            -> selection constraints
            -> procedural geometry and decoration
            -> JigsawConnectorDefinition[]
```

运行时分为四步：

1. 按世界种子和 placement region 确定候选起点；
2. 从起始模块的 socket 开始扩展 piece graph；
3. 对布局执行数量目标校验，必要时使用派生种子确定性重试；
4. 缓存布局，并只把与当前 `32 x 256 x 32` 体素柱相交的 piece 写入该柱。

布局只依赖世界种子、结构内容哈希和 region 坐标，不依赖区块流送顺序。

## 2. 资源与入口

默认资产：

- `Assets/Game/Config/StructureFeatures/Jigsaw/AbandonedMineshaft.asset`
- `Assets/Game/Config/StructureFeatures/Jigsaw/NetherFortress.asset`

新建入口：`Create > Supernova > World Generation > Jigsaw Structure`。

默认资产重建入口：
`Tools > Supernova > World Generation > Create Default Jigsaw Structures`。
重建器使用 `ProjectAssetPaths`，不使用硬编码绝对路径。

选中结构资产后，Inspector 会显示模块数、显式 socket 数、必需模块数，并执行图校验：

- 起始模块是否有出口；
- output socket 的主池或 fallback 池是否有兼容入口；
- 必需模块能否在 `maxDepth` 内出现；
- piece ID、connector ID 和首层强制模块是否合法。

## 3. Structure 参数

### Identity and Materials

- `enabled`：是否把该结构加入世界生成快照；关闭时有效生成概率为 0。
- `stableId`：稳定结构 ID，并参与缓存内容标识；修改它会产生独立缓存键。
- `primaryVoxelType`：外壳、地板等主体体素，必须为非 Air。
- `accentVoxelType`：支撑、书架、立柱、牢房栅栏、传送门框等装饰体素；为空时回退为主体体素。

### Placement

- `seedSalt`：该结构独立的随机盐；世界种子相同但盐不同会得到不同位置和布局。
- `regionSizeInChunks`：每个候选区域的边长，单位为体素柱区块。每个区域最多一个候选结构。
- `placementChance`：候选区域通过选址的概率。
- `minFloorHeight` / `maxFloorHeight`：起始 piece 地板高度范围。

候选点会在 region 内保留 `maxHorizontalDistance` 边距，因此必须满足：

```text
regionSizeInChunks * VoxelColumnChunkData.Width
    > 2 * maxHorizontalDistance
```

### Piece Graph

- `maxPieces`：整座结构最多 piece 数，包含起始 piece 和 terminator。
- `maxDepth`：connector graph 最大深度；起始 piece 深度为 0。
- `maxHorizontalDistance`：任一 piece 包围盒相对起点允许的最大水平距离。
- `firstPieceId`：可选的深度 1 强制模块。该模块必须在深度 1、目标池和 socket 匹配规则下合法。

### Layout Quality and Performance

- `layoutAttempts`：为满足 `minimumCount` 进行的确定性整图重试次数。结果选择顺序为“缺失目标最少，其次 piece 更多”。
- `connectorPlacementAttempts`：一个 socket 尝试不同候选模块的上限。
- `collisionPadding`：无父子关系 piece 之间额外保留的水平空隙。

## 4. Piece 参数

### Identity and Pool

- `stableId`：模块稳定 ID，同一结构内唯一。
- `displayName`：编辑器显示名。
- `poolId`：该模块可以被哪个目标池选中。
- `outputPoolId`：没有显式 socket 时，旧式推导出口使用的目标池。
- `startPiece`：是否为起始模块；每类结构必须恰好一个。
- `weight`：同池、同深度合法候选之间的整数权重。
- `minimumGraphDepth` / `maximumGraphDepth`：允许出现的闭区间深度。

### Selection Constraints

- `minimumCount`：期望整座结构至少出现的数量。布局器会在接近 `requiredByDepth` 或容量不足时优先选择，并用整图重试提高保证度。
- `maximumCount`：硬上限；0 表示不限。
- `allowConsecutive`：关闭后不能直接接在同类型父 piece 后面。
- `requiredByDepth`：未满足 `minimumCount` 时开始提高选择优先级的深度；0 使用结构的 `maxDepth`。

`maximumCount` 是硬约束；`minimumCount` 是布局有效性目标。编辑器会拒绝明显不可能的配置，测试和预览仍应覆盖实际空间碰撞下的目标满足率。

### Geometry and Decoration

- `Shape`：`Room`、`Corridor`、`Crossing`、`Stairs`、`VerticalShaft`。
- `BuildStyle.Excavated`：先雕空内部，再写支撑等 accent。
- `BuildStyle.Masonry`：依次执行 Shell、Air、Accent pass，确保相邻模块不会互相封门。
- `ConnectorPattern`：仅在 `connectors` 为空时使用的兼容模式。
- `Decoration`：`SupportFrames`、`LibraryShelves`、`Pillars`、`PrisonCells`、`PortalFrame`、`SpiralStairs` 等程序化装饰。

Box（`Room` / `Crossing` / `VerticalShaft`）使用宽、深、高范围；Passage 使用长度范围、宽、高和楼梯 `verticalDelta`。宽度会规范为奇数，楼梯现在也会在最小/最大长度范围内随机选择。`SpiralStairs` 仍可用于通用原型模块，但当前正式 `NetherFortress` 不使用楼梯或顶底板开孔：它使用 7×16×7 的空心 `VerticalShaft` 作为电梯井式 corridor，在井道侧墙的两个不同高度各开一个 3×5 门洞，并由 9×7×9 landing 承接另一端房间。

## 5. 显式 Socket

只要一个模块配置了至少一个 `JigsawConnectorDefinition`，该模块就完全使用显式 socket，不再读取 `ConnectorPattern`。

每个 socket 包含：

- `stableId`：模块内唯一的接口 ID。
- `role`：`Input`、`Output` 或 `Bidirectional`。
- `face`：`Forward`、`Right`、`Back`、`Left`、`Up`、`Down`；水平面随 piece yaw 转到世界方向，上下保持竖直。
- `joint`：预留的对齐语义；当前程序化布局使用 `Aligned`。
- `socketName` / `targetName`：名称匹配。`*` 为通配符；输出的 target 必须匹配输入的 name，输入的 target 也必须匹配输出的 name。
- `targetPoolId`：该出口的主候选池。
- `fallbackPoolId`：主池所有候选均失败后尝试的终止池。
- `alongOffset`：Passage 侧面 / 上下 socket 沿通道的偏移；-1 使用中点。
- `lateralOffset`：socket 表面内的横向偏移。
- `verticalOffset`：水平墙面开口底部相对地板的高度。
- `activationChance`：该出口进入 frontier 的概率。
- `openingWidth` / `openingHeight`：水平面为门洞宽高，上下表面为水平孔洞的宽度 / 前后长度。

候选模块会枚举所有兼容 input socket，计算旋转和平移，使父输出与子输入相邻对齐。水平面之间可用 yaw 互配；通用 `Up` 只匹配 `Down`、`Down` 只匹配 `Up`，piece 不会翻转。socket 激活只会把分支加入 frontier；只有子 piece 真正放置成功后，父输出与子输入才会同时进入 opening 列表并雕通两侧表面。`NetherFortress` 电梯链使用保留的 `fort_lift_*` socket 名称，这些名称必须精确匹配，不能由 DenseJigsaw 的 `*` 通配 socket 消费。对于只有一个必选出口的 `VerticalShaft`，生成器会在放置井道前预留一个 piece 数量、一个深度层级并检查 landing 的碰撞与边界；landing 连接还会优先于普通 frontier 处理。若完整链路无法放下，井道不会生成，源房间的墙面门洞也保持封闭。通用 `Down` opening 仍会阻止 `FoundationFill` / `SupportToGround` 回填孔洞。

## 6. 布局算法

当前布局器是适合矿洞、Stronghold、Nether Fortress 风格的 frontier piece graph：

1. 建立起始 piece，随机选择四向旋转；
2. 激活其输出 socket，加入 FIFO frontier；
3. 按 pool、深度、硬配额、连续重复规则和 socket 名称过滤候选；
4. 优先选择尚未满足且已到目标深度的模块，否则按 weight 选择；
5. 对齐 input socket，随机决定尺寸和楼梯升降；
6. 使用空间哈希查询附近 piece，再做精确整数 AABB 碰撞；父 piece 允许在连接面相接；
7. 成功后提交 piece 和它的输出 socket；失败则尝试其他候选，最后转入 fallback pool；
8. graph 结束后检查 minimum counts，不满足时使用派生种子重建，保留最优布局。

该过程对同一 `(worldSeed, feature content, region)` 完全确定。

## 7. 性能设计

### 布局缓存

缓存键包含：

```text
feature.ContentHash + worldSeed + regionX + regionZ + placementCentre
```

`ContentHash` 覆盖结构参数、材料 ID、全部模块、配额、尺寸和 socket，因此编辑资产后不会错误复用旧布局。

缓存使用 `ConcurrentDictionary<key, Lazy<layout>>`：多个后台区块任务同时请求同一区域时只构建一次。缓存最多保留 512 个区域，按插入顺序淘汰，避免无限世界流送造成无界内存增长。

### 空间索引

- 布局碰撞使用 16 体素 cell 的 spatial hash，只扫描与候选包围盒相邻的 piece。
- 缓存条目预先建立“体素柱坐标 -> 相交 piece[]”索引。
- rasterizer 的 Shell/Air/Accent 三个 pass 只遍历当前柱相交的 piece，而不是遍历整座结构。

当前编辑器实测（同一机器、fortress 36 pieces）：100 个冷布局约 28 ms；随后 1000 次缓存命中约 0.18 ms；64 个并发同键请求的实际布局构建次数为 1。数值只用于回归参考，不作为跨机器硬阈值。

## 8. 默认结构内容

### Abandoned Mineshaft

- `mineshaft_room`：四向起始房间。
- `mineshaft_corridor`：变长通道、木支撑、概率侧分支。
- `mineshaft_crossing`：三向扩展节点。
- `mineshaft_stairs`：跨高度通道。
- `mineshaft_storage`：较大的终止储藏洞室。
- `mineshaft_dead_end`：`terminators` fallback pool 中的塌方封口。

### Fortress

- `fortress_lobby`：带内柱的四向大厅。
- `fortress_hall`：宽走廊和概率侧支路。
- `fortress_crossing`：带柱的三向交叉厅。
- `fortress_stairs`：上下层连接。
- `fortress_vertical_up_shaft` / `fortress_vertical_down_shaft`：由低概率墙面门洞进入的空心电梯井式 corridor；上下门洞相差 10 个体素，不生成楼梯，也不破坏房间楼板。
- `fortress_vertical_landing`：井道另一端的完整落地房间，随后重新进入水平 corridor 池，禁止连续堆叠井道。
- `fortress_library`：带环形书架的终止房间，数量 1～2。
- `fortress_prison`：带程序化牢房栅栏的稀有终止房间，最多 2 个。
- `fortress_portal_room`：带门框的必需目标房间，最多且至少 1 个。
- `fortress_dead_end`：fallback 封口走廊。

默认 fortress 在采样区域中会生成到 `maxPieces=36`，并满足 hall、crossing、stairs、library 和唯一 portal room 的数量目标。

## 9. 仍然存在的边界

本版本显著增强了 Stronghold / Nether Fortress / Mineshaft 风格的程序化 piece graph，但没有假装覆盖所有 Minecraft 结构范式：

- 尚未把 `VoxelStructureAsset` 作为可旋转 piece template 接入 jigsaw；任意建筑外形仍需扩展程序化 rasterizer。
- 尚无 terrain matching、地基向下延伸、生物群系过滤、同心环 placement 或 structure-set 竞争。
- 尚无 loot、spawner、rail、door、light、entity 等 marker/processor pipeline。
- 尚无 End City 风格“一条规则产生多个 piece、整体碰撞、失败整组回滚”的递归语法布局器。
- `minimumCount` 在空间完全不可行时会返回缺失最少的布局，而不是突破碰撞边界强塞模块；这应由资产校验和种子批量测试提前发现。

下一阶段应保持 placement、缓存、空间索引和 rasterizer 基础设施共享，再分别增加 Template Jigsaw 与 Recursive Grammar strategy，而不是把所有结构语义继续塞进同一个随机队列。

## 10. 验证覆盖

`JigsawStructureGeneratorTests` 覆盖：

- 默认世界资产接线；
- 显式 socket、pool 和 graph validator；
- 矿洞与 fortress 的模块丰富度；
- 同种子完整模块图确定性；
- minimum/maximum count；
- library、support frame、侧分支开口和 masonry 室内的实际体素；
- 同键缓存复用、并发只构建一次、缓存容量上限；
- fortress lobby、hall、library、prison、portal room 的组合生成。
