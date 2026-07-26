# Minecraft 矿物生成与项目体素链路

## 1. 调研基线

调研基于 2026-07-26 时 Mojang 版本清单中的 Java Edition 最新正式版
`26.2`（发布时间 2026-06-16）。

核对材料：

- [Mojang Java 26.2 发布说明](https://www.minecraft.net/en-us/article/minecraft-java-edition-26-2)
- [Mojang 官方版本清单](https://piston-meta.mojang.com/mc/game/version_manifest_v2.json)
- [Java 26.2 官方 server.jar](https://piston-data.mojang.com/v1/objects/823e2250d24b3ddac457a60c92a6a941943fcd6a/server.jar)
- server.jar 大小：`60,894,273` bytes
- server.jar SHA-1：`823e2250d24b3ddac457a60c92a6a941943fcd6a`

本文直接检查了官方 jar 中的 worldgen JSON、`OreFeature` 和
`OreVeinifier`，不把社区矿层图当作算法依据。

## 2. Minecraft 的两套矿物生成机制

Java 26.2 的主世界矿物不是由一种算法统一生成，而是两套机制叠加：

1. 普通矿团：数据驱动的 placed feature + configured feature，最终调用
   `OreFeature` 生成局部矿团。
2. 大型矿脉：地形噪声管线中的 `OreVeinifier`，生成连续、稀有、带伴生岩的
   铜矿脉和铁矿脉。

普通铁矿或铜矿仍可在大型矿脉之外生成。大型矿脉不是把普通矿团的 `size`
调大。

## 3. 普通矿团的生成管线

### 3.1 Placed feature 决定“尝试放在哪里”

典型 placed feature 依次应用四类 placement modifier：

```text
count 或 rarity_filter
        ↓
in_square
        ↓
height_range
        ↓
biome
```

- `count`：每个生成区域发起固定或随机次数的尝试。
- `rarity_filter`：按 `1 / chance` 的概率发起一次尝试。
- `in_square`：在当前 16×16 水平区域内随机选择 X/Z。
- `height_range`：根据高度分布选择 Y。
- `biome`：只允许当前生物群系 generation settings 中声明的 feature 落地。

高度提供器常见两种：

- `uniform`：范围内每个 Y 等概率。
- `trapezoid`：中间更密集；未配置 plateau 时表现为三角分布。

同一种矿物可以有多次独立 pass。例如钻石同时有 small、medium、large 和
buried 四个 pass。矿物的最终分布是这些 pass 的叠加，不能只用一条高度曲线
完整描述。

### 3.2 Configured feature 决定“生成什么”

`minecraft:ore` configured feature 包含：

- `targets`：可替换方块规则和对应生成的矿石状态；
- `size`：沿矿团轴线采样的椭球数量/尺度参数；
- `discard_chance_on_air_exposure`：候选矿石邻接空气时的丢弃概率。

主世界通常同时配置：

- `minecraft:stone_ore_replaceables` → 普通石质矿石；
- `minecraft:deepslate_ore_replaceables` → 深板岩矿石。

因此“石头版本”和“深板岩版本”不是先生成矿物再按高度换贴图，而是目标规则
匹配到不同基岩后直接写入不同方块状态。

### 3.3 `OreFeature` 的矿团形状

官方 `OreFeature` 的核心过程是：

1. 在 `[0, π)` 中随机选择一条水平轴线方向；
2. 轴线长度随 `size` 增大，两个端点的 Y 各自有小幅随机偏移；
3. 沿轴线插值出 `size` 个中心点；
4. 每个中心点用正弦包络和随机数得到半径，形成一串重叠球/椭球；
5. 删除被其他球完全包含的球，避免重复扫描；
6. 遍历椭球覆盖的方块，每个坐标只处理一次；
7. 只有命中 target rule，并通过空气暴露规则时，才替换成矿石。

因此 `size` 不是“保证生成 N 个方块”。目标岩层、洞穴暴露、椭球重叠、世界
边界和随机半径都会让实际矿石数量变化。

### 3.4 空气暴露抑制

`discard_chance_on_air_exposure` 只在候选位置邻接空气时产生影响：

- `0.0`：跳过空气检查，矿石可正常暴露；
- `0.5`：邻接空气的候选约有一半被丢弃；
- `1.0`：邻接空气的候选全部被丢弃。

这就是钻石、青金石等矿物在洞壁上更少见的实现基础。它不是整体降低矿物
数量，而是有条件地降低“暴露矿石”的数量。

## 4. Java 26.2 主世界 placed feature 摘要

下表保留官方 JSON 的高度锚点写法。`above_bottom`、`below_top` 会随维度构建
高度解析，并在合法高度内取值。

| 矿物 pass | 尝试 | 高度分布 | configured size | 空气暴露丢弃 |
|---|---:|---|---:|---:|
| Coal upper | 30 | uniform: 136 → below_top 0 | 17 | 0 |
| Coal lower | 20 | trapezoid: 0 → 192 | 17 | 0.5 |
| Iron upper | 90 | trapezoid: 80 → 384 | 9 | 0 |
| Iron middle | 10 | trapezoid: -24 → 56 | 9 | 0 |
| Iron small | 10 | uniform: above_bottom 0 → 72 | 4 | 0 |
| Copper | 16 | trapezoid: -16 → 112 | 10 | 0 |
| Copper large variant | 16 | trapezoid: -16 → 112 | 20 | 0 |
| Gold | 4 | trapezoid: -64 → 32 | 9 | 0.5 |
| Gold lower | uniform 0..1 | uniform: -64 → -48 | 9 | 0.5 |
| Gold extra | 50 | uniform: 32 → 256 | 9 | 0 |
| Redstone | 4 | uniform: above_bottom 0 → 15 | 8 | 0 |
| Redstone lower | 8 | trapezoid: above_bottom -32 → 32 | 8 | 0 |
| Lapis | 2 | trapezoid: -32 → 32 | 7 | 0 |
| Lapis buried | 4 | uniform: above_bottom 0 → 64 | 7 | 1 |
| Diamond small | 7 | trapezoid: above_bottom -80 → 80 | 4 | 0.5 |
| Diamond medium | 2 | uniform: -64 → -4 | 8 | 0.5 |
| Diamond large | rarity 1/9 | 同 Diamond small | 12 | 0.7 |
| Diamond buried | 4 | 同 Diamond small | 8 | 1 |
| Emerald | 100 | trapezoid: -16 → 480 | 3 | 0 |

生物群系过滤仍是最终分布的重要组成：

- large copper variant 用于滴水石洞穴；
- gold extra 用于恶地；
- emerald 由山地生物群系选择。

所以表中的 count 不能脱离 biome 覆盖范围解释成“每个区块都有这么多矿团”。

## 5. 大型铜/铁矿脉

大型矿脉在噪声地形填充阶段执行，而不是走 `OreFeature`：

- `vein_toggle` 使用 `ore_veininess` 噪声决定矿脉类型和主轮廓；
- 正值分支是铜矿脉，范围 Y=0..50，伴生填充物为花岗岩；
- 负值分支是铁矿脉，范围 Y=-60..-8，伴生填充物为凝灰岩；
- 绝对 veininess 需要达到约 `0.4`，并在高度边缘 20 格内逐渐收窄；
- 每个候选点还有 0.7 的 solidness 门控；
- `vein_ridged` 切断部分区域，形成稀疏分支而不是实心噪声块；
- richness 随 veininess 从约 0.1 增到 0.3；
- `vein_gap < -0.3` 时跳过矿石；
- 成功的矿石位置有 2% 概率变成对应粗矿块；
- 未放矿石但属于矿脉结构的位置可变成花岗岩/凝灰岩。

这套机制适合大尺度、连续且可追踪的地质结构；普通矿团适合大量局部、可独立
配置的资源点。

## 6. 当前项目的体素生成链路

正式入口是 `MinecraftCaveInfiniteWorld`：

```text
worldSeed + 绝对体素坐标
        ↓
MinecraftCaveNoise
        ↓
MinecraftCaveDensityField.SampleFeatureDensity(Combined)
        ↓
后台任务生成 float[32³] + VoxelTypeId[32³]
        ↓
实心样本先赋 Base rock type
        ↓
MinecraftOreFeatureGenerator 放置普通矿团
        ↓
主线程 CommitChunk
        ↓
InfiniteVoxelWorld / VoxelChunkData / VoxelVolume
        ↓
MarchingCubesMesher.BuildChunk
        ↓
按 VoxelTypeId 分 submesh
        ↓
VoxelTypeCatalog 解析材质与挖掘耐久度
```

关键事实：

- 区块固定保存 `32 × 32 × 32` 个样本；
- 每个样本同时有 `float density` 和 `VoxelTypeId type`；
- 后台 `ChunkGenerationResult` 同时携带 `Densities` 和 `Types`；
- `CommitChunk` 使用 `SetSample` 同时提交密度和类型，不再把所有实心样本归一化
  成 `VoxelTypeId.Default`；
- 当前场景的自然基岩为 `Stone`（ID 2），矿团结果为 `Ore`（ID 3），空气仍为
  `VoxelTypeId.Air`；
- 固定结构、玩家放置和测试代码可以显式写入非默认类型；
- `MarchingCubesMesher` 已支持按类型分别 polygonise 并生成独立 submesh；
- `VoxelTypeUtility.ResolveMaterials` 已支持按类型分配材质；
- 挖掘代码已通过 catalog 按类型解析耐久度。

所有 `ScriptableObject` 都只在主线程读取。世界初始化时会把矿物资产转换为只含
数值和 `VoxelTypeId` 数组的 `MinecraftOreFeatureSettings` 快照，后台任务不访问
Unity 资产。

## 7. 配置资产

`VoxelTypeDefinition` 现在是独立 `ScriptableObject`，每份资产配置：

- 稳定数值 ID；
- 显示名称；
- 耐久度（当前项目中硬度的实际游戏语义：所需挖掘次数）；
- 材质。

`MinecraftVoxelTypes.asset` 只保存定义资产引用，不再内嵌多个定义。当前资产：

```text
Assets/Game/Config/VoxelTypes/Default.asset  -> ID 1, Default, 1
Assets/Game/Config/VoxelTypes/Stone.asset    -> ID 2, Stone,   4
Assets/Game/Config/VoxelTypes/Ore.asset      -> ID 3, Ore,     8
```

`Ore.asset` 指向独立的
`Assets/Game/Materials/Voxels/Ore.mat`，因此矿石 submesh 与灰色的运行时基岩
材质可直接区分。未指定材质的体素类型继续使用 chunk fallback material。

普通矿团的“怎样生成”不放进体素类型资产，而由独立的
`VoxelOreFeatureDefinition` 配置：

- 生成结果体素类型；
- 可替换的基岩类型列表；
- 独立 seed salt；
- 每个 16×16 水平放置区域的尝试次数；
- 每次尝试的发生概率；
- uniform / trapezoid 高度分布、最低/最高高度和 plateau；
- 普通矿团 `size`；
- `discardChanceOnAirExposure`。

```text
Assets/Game/Config/OreFeatures/Ore.asset
    result:       Ore
    replaceable:  Stone
    attempts:     8 / 16×16 region
    chance:       1
    height:       trapezoid, -64..64, plateau 0
    size:         8
    air discard:  0.5
```

`InfiniteCaves.scene` 引用 `Stone.asset` 作为 base solid，并启用上述
`OreFeature`。列表可以继续加入其他独立矿物 pass；执行顺序就是列表顺序，后续
pass 仍必须命中自己的 replaceable type 才能覆盖现有类型。

## 8. 普通矿团实现

`MinecraftOreFeatureGenerator` 复现普通 `OreFeature` 的核心形状：

1. 在 `[0, π)` 选择水平轴线角度；
2. 根据 `size / 8` 得到轴线两端，端点 Y 带小幅随机偏移；
3. 沿轴线插值 `size` 个中心；
4. 使用正弦包络、随机尺度和 `size / 16` 生成重叠球；
5. 去掉完全包含于其他球的球；
6. 每个候选样本只处理一次；
7. 仅替换“密度为实心且当前类型属于 replaceable 列表”的样本；
8. 候选暴露于六邻域空气时，按配置概率丢弃。

高度提供器与官方逻辑一致：

- `uniform` 在闭区间内等概率取整数高度；
- `trapezoid` 把剩余高度范围拆成上下两个均匀随机量相加；plateau 为 0 时形成
  三角分布，plateau 覆盖整个范围时退化为 uniform。

### 8.1 确定性与跨 Chunk

放置随机数只依赖：

```text
worldSeed + feature seedSalt + 16×16 region X/Z + attempt index
```

没有使用 `UnityEngine.Random`，也没有共享可变的随机数状态。

一个 32³ 项目 Chunk 会重放所有可能影响本区的周边 16×16 宿主区域候选，但只写
自己的 `VoxelTypeId[]`。相邻 Chunk 因而能独立得到同一条越界矿团：

- 后台任务不互相写邻居；
- Chunk 完成顺序不会改变结果；
- 负坐标使用 floor/ceil division 正确选择宿主区域；
- 空气暴露检查在本 Chunk 内读取密度数组，在边界外按绝对坐标重新采样
  `MinecraftCaveDensityField`，不会把尚未加载的邻居当空气。

## 9. 验证

EditMode 测试覆盖：

- 同种子生成完全一致的类型数组；
- 只替换配置的基岩类型；
- 相邻 Chunk 反向生成仍得到相同结果，并存在跨边界连续矿团；
- 空气暴露丢弃率为 1 时拒绝全部暴露候选；
- 默认矿团资产的类型引用、参数与矿石材质；
- `InfiniteCaves.scene` 的 Stone base 和 OreFeature 引用。

实际 Play Mode 冒烟检查在 220 个已生成 Chunk 中统计到 Stone 与 Ore 类型，且
控制台无错误，证明矿团已进入正式异步生成和提交链路，而不只是独立算法测试。

大型 `OreVeinifier` 矿脉仍不在本次范围内；如果以后实现，应作为独立 feature，
不应通过无限增大普通矿团 `size` 模拟。

1. 后台结果同时携带 densities 和 types，避免主线程再做整块扫描。
2. 随机性只依赖 `worldSeed + oreId + 绝对候选坐标`，不使用
   `UnityEngine.Random`。
3. 矿团跨 Chunk 时不能由两个并发任务互相写邻居。可按固定“候选宿主区域”
   生成矿团，每个 Chunk 独立判断本区样本是否落在该矿团内。
4. Chunk 必须检查周围宿主区域的候选，保证跨边界矿团连续且与生成顺序无关。
5. target rule 至少要检查“当前样本为实心且属于可替换基岩类型”。
6. 若实现空气暴露抑制，要读取候选的六邻域密度；Chunk 边界必须使用绝对坐标
   重新采样或读取已确定的邻域密度，不能把未加载邻居误判为空气。
7. 普通矿团参数和 `VoxelTypeDefinition` 应继续分离：前者描述“如何生成”，
   后者描述“生成后的体素是什么、怎样显示和挖掘”。
8. 大型矿脉应作为后续单独 feature，不应通过无限增大普通矿团 size 模拟。
