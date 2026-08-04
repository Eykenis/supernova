# Jigsaw 结构配置手册

> 面向策划。本手册说明如何**不写代码**（或几乎不写代码）配置一个全新结构：
> 从新建资产、摆模块、连接口，到接入世界、验证效果。
>
> 想了解算法内部如何运作，见同目录 `Jigsaw结构生成.md`。

## 0. 心智模型

一个结构 = **一份结构资产** + **若干模块** + **模块之间的接口**。

生成器做的事很简单：

1. 在世界上按规则挑一些"起点"；
2. 从起始模块的出口开始，不断"接下一块"，像拼乐高；
3. 接不上或撞到别的模块就换一块，都换不上就封口；
4. 拼好之后才写体素。

你要配置的，就是"有哪些块"、"哪块能接哪块"、"每块出现多少个"。

三个概念先记住，后面全靠它们：

| 概念 | 含义 | 类比 |
|---|---|---|
| **Pool（池）** | 模块的分类标签。出口指定"我要从哪个池里抽" | 抽卡卡池 |
| **Socket（接口）** | 模块墙上的一个门洞，有朝向、名字、目标名 | 插头 / 插座 |
| **Weight（权重）** | 同池同深度的候选之间的相对抽中概率 | 卡池里的稀有度 |

## 1. 五分钟上手：做一个最小结构

目标：一个房间 + 若干走廊，能生成、能进人。

### 1.1 新建资产

在 Project 窗口右键 → `Create > Supernova > World Generation > Jigsaw Structure`。

建议放在 `Assets/Game/Config/StructureFeatures/Jigsaw/` 下，和现有的
`AbandonedMineshaft` / `Fortress` 同级。

### 1.2 填结构级参数

| 字段 | 填什么 | 说明 |
|---|---|---|
| Enabled | ✅ | 取消勾选即完全不生成 |
| Stable Id | `my_ruin` | 唯一英文 ID，改它会导致缓存失效重算 |
| Primary Voxel Type | `StructureBrick` | 外壳、地板的体素 |
| Accent Voxel Type | `FortressBrick` | 装饰体素；留空则回退为 Primary |
| Seed Salt | 任意整数，如 `20260804` | **务必与其他结构不同**，否则位置会重叠 |
| Region Size In Chunks | `10` | 每 10×10 区块最多一座 |
| Placement Chance | `0.4` | 每个候选区域 40% 概率成立 |
| Min / Max Floor Height | `48` / `160` | 起始房间地板的高度范围 |
| Max Pieces | `24` | 整座结构最多多少块 |
| Max Depth | `6` | 从起点算起最多接几层 |
| Max Horizontal Distance | `100` | 允许向外延伸多远（体素） |

⚠️ 有一条硬约束必须满足，否则资产报错：

```text
Region Size In Chunks × 32  >  Max Horizontal Distance × 2
```

例：`10 × 32 = 320 > 100 × 2 = 200` ✅

（`Max Horizontal Distance` 在 Inspector 里会自动被夹到区域半径以内，
所以填过大的值不会报错而是被截断。默认矿洞与 fortress 用的是
`region = 4`、`maxHorizontalDistance = 63`，即 `128 > 126`，刚好成立。）

### 1.3 加模块

Inspector 底部有四个按钮：`Add Room` / `Add Corridor` / `Add Crossing` / `Add Stairs`。
点两次，加一个 Room 和一个 Corridor。展开 `Piece Modules` 逐个改。

**模块 1：起始房间**

| 字段 | 值 |
|---|---|
| Stable Id | `ruin_hall` |
| Pool Id | `ruin` |
| Start Piece | ✅ |
| Build Style | `Masonry` |
| Box Dimensions | 宽/深 `13`，高 `7` |

**模块 2：走廊**

| 字段 | 值 |
|---|---|
| Stable Id | `ruin_corridor` |
| Pool Id | `ruin` |
| Start Piece | ❌ |
| Weight | `10` |
| Minimum / Maximum Graph Depth | `1` / `6` |
| Build Style | `Masonry` |
| Passage Dimensions | 长 `8`~`14`，宽 `3`，高 `4` |

### 1.4 连接口

这一步是关键。每个模块展开 `Explicit Sockets`，手动添加。

**给房间加 4 个出口**（东南西北各一）：

| 字段 | 值 |
|---|---|
| Stable Id | `north` / `east` / `south` / `west` |
| Role | `Output` |
| Face | `Forward` / `Right` / `Back` / `Left` |
| Socket Name | `ruin_branch` |
| Target Name | `ruin_entry` |
| Target Pool Id | `ruin` |
| Opening Width / Height | `3` / `3` |
| Activation Chance | `1` |

**给走廊加 1 个入口 + 1 个出口**：

入口：

| 字段 | 值 |
|---|---|
| Stable Id | `entrance` |
| Role | `Input` |
| Face | `Back` |
| Socket Name | `ruin_entry` |
| Target Name | `ruin_branch` |
| Opening Width / Height | `3` / `3` |

出口：

| 字段 | 值 |
|---|---|
| Stable Id | `forward` |
| Role | `Output` |
| Face | `Forward` |
| Socket Name | `ruin_branch` |
| Target Name | `ruin_entry` |
| Target Pool Id | `ruin` |

### 1.5 匹配规则（务必理解）

两个 socket 能连上，必须**双向**都对得上：

```text
输出.Target Name  ==  输入.Socket Name
输入.Target Name  ==  输出.Socket Name
```

`*` 是通配符，任何名字都能匹配。

上面的例子里：
- 房间出口：name=`ruin_branch`, target=`ruin_entry`
- 走廊入口：name=`ruin_entry`, target=`ruin_branch`
- 交叉验证：`ruin_entry == ruin_entry` ✅ 且 `ruin_branch == ruin_branch` ✅

**命名建议**：统一用 `<族名>_branch`（出口）配 `<族名>_entry`（入口）。
需要特殊通道时再引入第三个名字，比如只有楼梯能接的 `ruin_vertical`。

### 1.6 接入世界

打开 `Assets/Game/Config/Worlds/DefaultWorldGeneration.asset`，
在 `Jigsaw Structures` 列表末尾追加你的资产。

### 1.7 检查

选中结构资产，Inspector 会显示：

```text
Valid structure: 2 modules, 6 explicit sockets, 0 required placements.
```

红色 Error 必须清零，黄色 Warning 逐条判断是否符合预期。常见提示见第 7 节。

## 2. 让结构变丰富

### 2.1 数量控制

| 字段 | 作用 | 性质 |
|---|---|---|
| Minimum Count | 期望整座结构至少出现几个 | **目标**，靠整图重试争取 |
| Maximum Count | 硬上限，`0` = 不限 | **硬约束**，绝不突破 |
| Required By Depth | 从第几层开始优先塞这个模块（`0` = 用 Max Depth） | 优先级触发点 |
| Allow Consecutive | 关闭后不能紧接同类型模块 | 防止一长串一样的走廊 |

典型配法：

```text
主走廊    Minimum=6  Maximum=0  Required By Depth=3   Allow Consecutive=✅
交叉厅    Minimum=2  Maximum=8  Required By Depth=5   Allow Consecutive=❌
宝库      Minimum=1  Maximum=1  Required By Depth=6   Allow Consecutive=❌
稀有房间  Minimum=0  Maximum=2  —                     Allow Consecutive=❌
```

⚠️ `Σ 所有 Minimum Count + 1 <= Max Pieces`，否则资产报错。

**"必达房间"的正确做法**：`Minimum=1, Maximum=1, Required By Depth` 设为
接近 `Max Depth`。生成器会在深度足够或名额告急时优先选它，并在整图层面
重试 `Layout Attempts` 次来提高达成率。它**不会**为了塞进去而破坏碰撞规则；
如果你的空间确实放不下，应该调大 `Max Horizontal Distance` 或减小房间尺寸。

### 2.2 封口模块（terminator）

没有封口时，接不上的分支会留下一个"墙上有门洞但门后什么都没有"的位置。
做法：

1. 建一个模块，`Pool Id` = `terminators`，`Connector Pattern` = `None`，
   只加一个 Input socket，且该 socket 的 `Target Pool Id` 也填 `terminators`；
2. 在所有 Output socket 上把 `Fallback Pool Id` 填 `terminators`。

主池所有候选都失败后，生成器会转向 fallback 池，用这个小模块封住开口。
默认矿洞的 `mineshaft_dead_end`（塌方封口）和 fortress 的
`fortress_dead_end`（封死走廊）就是这么配的。

### 2.3 多池分区

用池把结构分成语义区域，形态会自然分层。原版下界要塞的"开阔桥区 → 封闭
走廊区"就是双池：

```text
入口大厅.出口  ->  Target Pool = bridge     (开阔桥梁、平台、交叉口)
桥.某个出口    ->  Target Pool = corridor   (封闭走廊、转角、小房间)
走廊.出口      ->  Target Pool = corridor
```

一个模块的 `Pool Id` 决定它**能被谁抽中**；socket 的 `Target Pool Id`
决定这个出口**从哪抽**。两者是不同字段，别混。

### 2.4 概率分支

Output socket 的 `Activation Chance` 控制该出口是否进入生长队列。
主通道填 `1`，侧向分支填 `0.3` 左右，即可得到"主干清晰、偶有岔路"的形态。

### 2.5 装饰

`Decoration` 字段用 Accent 体素写程序化装饰：

| 值 | 效果 | 适用 |
|---|---|---|
| `None` | 无 | 走廊 |
| `SupportFrames` | 木支撑架（立柱 + 顶梁），间距由 `Decoration Spacing` 控制 | 矿洞通道 |
| `LibraryShelves` | 沿墙环形书架 | 图书室 |
| `Pillars` | 内部立柱 | 大厅 |
| `PrisonCells` | 牢房栅栏（中间留门） | 监牢 |
| `PortalFrame` | 传送门框 | 目标房间 |

### 2.6 Build Style 怎么选

| Style | 行为 | 观感 | 用在 |
|---|---|---|---|
| `Excavated` | 只雕空，不砌墙 | 直接在岩石里挖出来 | 矿洞、天然洞室 |
| `Masonry` | 砌外壳 + 雕空 + 装饰 | 独立砌造的建筑 | 堡垒、要塞、遗迹 |

## 3. 落地处理器（Processors）

处理器在结构写完后运行，用来解决"结构悬空 / 被地形封顶 / 石砖太单调"。
它们**不影响布局碰撞**，所以一根很深的柱子不会导致模块被拒绝。

每个模块可加多个，字段：

| 字段 | 说明 |
|---|---|
| Kind | 类型，见下表 |
| Palette | `Primary` 或 `Accent`，决定写哪种体素 |
| Maximum Distance | 最多走多少格 |
| Inset | 从包围盒向内收缩多少格 |
| Chance | 逐体素生效概率 |
| Perimeter Only | 仅对 SupportToGround 有意义：只在边缘落柱 |

| Kind | 行为 | 典型用法 |
|---|---|---|
| `SupportToGround` | 向下写实体，**遇到已有地形就停** | 大房间、桥、平台的支柱。`Maximum Distance` = 20~24 |
| `FoundationFill` | 向下写固定厚度地基，不探测地形 | 房间底部加 3 格地基，`Perimeter Only` = ❌ |
| `ClearAbove` | 向上雕空固定高度 | 防止地形贴住屋顶，`Maximum Distance` = 2 |
| `Weathering` | 按概率替换成 Accent 调色 | 石砖混色。`Palette` = `Accent`，`Chance` = 0.18~0.3 |

**Weathering 生效前提**：`Accent Voxel Type` 必须与 `Primary Voxel Type`
**不同**，否则换了也看不出来（校验会给 Warning）。它只会改本结构写过的
体素，不会误伤周围岩石或矿脉。

默认 fortress 的配法可以直接参考：

```text
fortress_lobby        SupportToGround(20) + Weathering(Accent, 0.22)
fortress_hall         SupportToGround(24) + Weathering(Accent, 0.18)
fortress_library      FoundationFill(3, 全面) + Weathering(Accent, 0.25)
fortress_portal_room  SupportToGround(24) + ClearAbove(2) + Weathering(Accent, 0.30)
mineshaft_storage     SupportToGround(16)
```

## 4. 放宝藏和怪物（Spawn Markers）

世界的自然散布不知道结构存在，所以它不会保证"传送门房间有 Boss"或
"图书室台座上有战利品"。用 spawn marker 手工指定。

每个模块展开 `Spawn Markers` 添加，字段：

| 字段 | 说明 |
|---|---|
| Stable Id | 模块内唯一 |
| Kind | `Treasure` 或 `Monster` |
| Treasure | Kind = Treasure 时指定 `TreasureDefinition` 资产 |
| Monster | Kind = Monster 时指定 `MonsterSpawnDefinition` 资产 |
| Local Offset | 相对模块原点的偏移，**在模块自身坐标系内**（见下） |
| Yaw | 在模块朝向之上再叠加的旋转角度 |
| Spawn Chance | 这个 marker 是否触发的概率 |
| Count | 生成几个 |
| Scatter Radius In Voxels | Count > 1 时其余实例的散开半径 |
| Snap To Floor | 是否向下吸附到第一个可站立的地面 |
| Floor Search Distance | 向下找多少格 |

### 4.1 Local Offset 怎么理解

偏移用的是**模块自己的坐标系**，会随模块旋转：

- `x` = 模块的右方向
- `y` = 上（世界上方，不旋转）
- `z` = 模块的前方向（`Face.Forward` 指向的那边）

所以 `(2, 1, 3)` 意思是"从模块原点向右 2、向上 1、向前 3"。模块无论朝
哪个方向生成，宝箱都在同一个相对位置。**不要**把它当世界坐标填。

模块原点在哪：

- Room / Crossing：房间中心，`y` 为地板高度；
- Corridor / Stairs：通道起点（入口那一端）的中心线，`y` 为地板高度。

所以"房间中央地板上放一个宝箱"就是 `(0, 1, 0)`。

### 4.2 Snap To Floor

打开时（推荐），生成器从 marker 位置向下最多找 `Floor Search Distance` 格，
落在第一个"下方是实体、自身是空气"的位置。找不到就**不生成**这个实例。

这让你不必精确对齐地板高度——大致放在房间里、勾上 Snap To Floor 就行。
对于本来就该悬空的东西（吊灯、飞行怪）关掉它。

⚠️ 关掉 Snap To Floor 时请确认 marker 位置确实是空气，否则宝箱会嵌在墙里。

### 4.3 怪物名额

marker 怪物用**独立名额**，不占用自然生成的额度：

- `MonsterSpawnTable.Maximum Active Monsters` — 自然散布的上限
- `MonsterSpawnTable.Maximum Marker Monsters` — 结构 marker 的上限

好处是世界里怪物再多，你设计的 Boss 房照样会出怪。但 marker 名额也有上限，
超出后新的 marker 怪物会被跳过。如果地图里 Boss 房很多，把
`Maximum Marker Monsters` 调大。

### 4.4 典型配法

**Boss 房（传送门房间放一只精英怪）**

```text
Stable Id = portal_boss
Kind = Monster
Monster = <你的精英怪 MonsterSpawnDefinition>
Local Offset = (0, 1, 0)
Spawn Chance = 1
Count = 1
Snap To Floor = ✅   Floor Search Distance = 6
```

**图书室战利品（两件宝藏散在房间里）**

```text
Stable Id = library_loot
Kind = Treasure
Treasure = <你的宝藏 TreasureDefinition>
Local Offset = (0, 1, 0)
Spawn Chance = 1
Count = 2
Scatter Radius In Voxels = 3
Snap To Floor = ✅
```

**走廊里偶遇的小怪（一半概率、三只一群）**

```text
Stable Id = hall_patrol
Kind = Monster
Local Offset = (0, 1, 4)      # 从通道起点向前 4 格
Spawn Chance = 0.5
Count = 3
Scatter Radius In Voxels = 2
Snap To Floor = ✅
```

### 4.5 在模板里放 marker

跟 socket 一样，marker 也能存在体素模板里（见第 5 节）。模块若未配置自己的
marker，会自动继承模板的。这样手绘的房间连带它的战利品一起复用。

模板资产的 `Spawn Markers` 列表用法与上表完全相同，`Local Offset` 以模板
`Anchor` 为原点。

### 4.6 注意事项

- **每柱只生成一次**：marker 不会因为你走开再回来而重复生成。
- **散开的实例可能跨柱**：`Count > 1` 且散开半径较大时，部分实例落在邻近
  区块。它们由那个区块自己生成，最终总数仍正确，但生成时机可能略有先后。
- **确定性**：同一世界种子下，同一个 marker 的结果永远相同。调 Spawn Chance
  验证效果时记得也换换种子。

## 5. 用手绘模板做任意形状（零编码）

程序化几何只有 Room / Corridor / Crossing / Stairs 四种盒体。想要任意
外形（雕像、破碎拱门、异形塔），用体素模板。

### 5.1 手绘一个模板

1. 打开场景 `Assets/Scenes/VoxelStructureEditor.scene`；
2. 选中 `Voxel Structure Authoring` 物体；
3. 新建模板资产：`Create > Supernova > Voxels > Voxel Structure`，
   拖到 `Structure To Edit`；
4. 设置 `Size`（各轴 ≤ 128）与 `Anchor`（对齐基准点，通常放底面中心）；
5. 设置 `Paint Voxel Type` 与 `Paint Density`；
6. 场景中 **Shift + 左键**加体素，**Ctrl + 左键**删体素；
   也可用 `Add Voxel At Anchor` 按钮；
7. 点 `Save Structure` 写回资产。

### 5.2 在模板里标接口

这是"零编码"的关键：**把 socket 存在模板里**，用它的模块就自动继承，
不需要再手填一遍，也不会出现标记与几何脱节。

在 Inspector 的 `Jigsaw Template Sockets` 分区：

1. 把 `Anchor` 移到你想开门的那个体素位置；
2. 点 `Add Socket At Anchor`；
3. 展开资产的 `Sockets` 列表，改这个 socket 的
   `Face` / `Role` / `Socket Name` / `Target Name` / `Target Pool Id` /
   `Opening Width` / `Opening Height`。

场景视图会用青色线框 + 箭头画出每个 socket 及其朝向，可以直接目视核对。

### 5.3 在模块里用模板

1. 在结构资产里新建一个模块（点 `Add Room` 起手即可）；
2. 展开 `Optional Voxel Template`，把模板拖进 `Voxel Template`；
3. `Template Writes Air` 一般保持 ✅（让模板里的空气也写入，雕出内部空间）；
4. **不要**在该模块里再填 `Explicit Sockets` —— 留空即自动继承模板标记。
   一旦填了自己的 connector，模板标记就会被忽略。

模板会替代所有盒体尺寸字段（宽/深/高/长度都不再起作用），并保留模板里
每个体素**自己的类型**，不会被结构的 Primary 覆盖。

## 6. 选址策略

### 6.1 RandomSpread（默认，均匀散布）

适合矿洞、遗迹等"密度稳定"的结构。参数就是第 1.2 节的
`Region Size In Chunks` + `Placement Chance`。

想更稀有：减小 `Placement Chance` 或增大 `Region Size In Chunks`。

### 6.2 ConcentricRings（同心环）

适合"从出生点向外探索、发现密度可预期"的地标（原版末地要塞范式）。
把 `Placement Strategy` 改为 `ConcentricRings`，然后填：

| 字段 | 说明 | 参考值 |
|---|---|---|
| Ring Structure Count | 全世界总共几座 | `128` |
| Ring Count | 分几个环 | `8` |
| Ring Distance In Chunks | 每环半径步长（区块） | `32` |
| Ring Spread In Chunks | 环内径向抖动 | `3` |

外环名额自动多于内环，避免越远越稀疏。注意此策略下
`Region Size In Chunks` / `Placement Chance` 不再参与选址。

### 6.3 Structure Set（竞争）

想让两个结构"抢同一批位置、二者只出其一"（原版下界要塞 vs 堡垒遗迹）：

在**两个**结构资产上都填相同的 `Structure Set Id`，再各填
`Structure Set Weight`：

```text
结构 A：Structure Set Id = nether_complexes,  Weight = 2
结构 B：Structure Set Id = nether_complexes,  Weight = 3
```

结果：每个候选格恰好一个胜者，B 以 3:2 的比例更常见。
留空 `Structure Set Id` 的结构永远独占自己的格子，互不干扰。

## 7. 校验提示速查

选中结构资产，Inspector 会实时列出问题。

### 7.1 Error（必须修）

| 提示 | 原因 | 修法 |
|---|---|---|
| `Start piece has no output socket` | 起始模块没有 Output | 给它加至少一个 Output socket |
| `Template piece needs sockets` | 用了模板但两边都没接口 | 在模板里加 socket 标记，或给模块加 connector |
| `Required piece cannot appear within maxDepth` | 必需模块的深度区间与 Max Depth 无交集 | 调小模块的 `Minimum Graph Depth` 或调大结构 `Max Depth` |
| `duplicate processor ID` | 同模块内处理器 ID 重复 | 改名 |
| `duplicate spawn marker ID` | 同模块内 marker ID 重复 | 改名 |
| `has no Treasure/Monster prefab assigned` | marker 没指定资产 | 填上 `Treasure` 或 `Monster` 字段 |
| `placement region narrower than twice its layout radius` | 违反 1.2 的硬约束 | 增大 Region Size 或减小 Max Horizontal Distance |

资产还可能直接报红字异常（`TryCreateSettings` 失败）：

| 异常 | 修法 |
|---|---|
| `must have exactly one start piece` | 确保**恰好**一个模块勾选 Start Piece |
| `Duplicate jigsaw piece ID` | 模块 ID 必须互不相同 |
| `has duplicate connector ID` | 同一模块内 socket ID 必须互不相同 |
| `First piece does not exist` / `not eligible at graph depth 1` | `First Piece Id` 拼错，或该模块的深度区间不含 1 |
| `sum of minimum piece counts exceeds maxPieces` | 调小 Minimum Count 或调大 Max Pieces |
| `floor heights must leave room for the tallest piece` | 调低 Max Floor Height 或减小最高模块的高度 |
| `requires a solid primary voxel type` | Primary Voxel Type 为空或是 Air |

### 7.2 Warning（判断是否符合预期）

| 提示 | 含义 |
|---|---|
| `targets pool X, but no compatible piece can consume it` | 该出口注定接不上东西——池名拼错，或 socket 名字没配对 |
| `has a zero chance and never runs` | 处理器 Chance = 0 |
| `has a zero chance and never fires` | marker Spawn Chance = 0 |
| `places N instances with no scatter radius` | marker Count > 1 但散开半径为 0，会全部叠在一格 |
| `snaps to floor with a zero search distance` | marker 勾了 Snap To Floor 但搜索距离为 0，只在恰好贴地时才生成 |
| `weathers into the primary type` | Accent 与 Primary 相同，风化无视觉效果 |
| `can reach the world floor and will be truncated` | 处理器向下深度会触底被截断 |
| `fewer ring candidates than rings` | 环数比候选数还多，外环空置 |
| `places all rings within its own layout radius` | 环半径太小，候选必然互相重叠 |

## 8. 验证效果

### 8.1 预览场景

打开 `Assets/Scenes/WorldGenerationPreview.scene`，可在编辑器内直接看
生成结果，不必进游戏。

### 8.2 无限世界

打开 `Assets/Scenes/InfiniteCaves.scene` 进 Play。若结构稀有不好找，
临时把 `Placement Chance` 调到 `1`、`Region Size In Chunks` 调小，
验证完再改回。

### 8.3 多种子抽查

`Minimum Count` 是目标而非保证。改完配置后建议换几个世界种子各看一遍，
确认必达房间在大多数种子下都出现。若经常缺失：

- 调大 `Layout Attempts`（整图重试次数）；
- 调大 `Max Horizontal Distance`（给结构更多空间）；
- 减小必需房间的尺寸；
- 调低 `Required By Depth`（更早开始优先）。

## 9. 重建默认资产

**没有这个功能，也不需要。**

结构资产就是唯一的定义来源。项目里曾有一个
`Tools > Supernova > World Generation > Create Default Jigsaw Structures`
菜单用代码重建矿洞与 fortress，它已被删除——因为那意味着同一个结构存在
两份定义，一份在资产里、一份在代码里，两者会漂移。事实上它们已经漂移过：
资产引用的砖块与脚本写的不是同一种。

想参考默认结构怎么配，**直接打开资产看 Inspector**：

- `Assets/Game/Config/StructureFeatures/Jigsaw/AbandonedMineshaft.asset`
- `Assets/Game/Config/StructureFeatures/Jigsaw/Fortress.asset`

需要一个新结构就复制一份资产（Ctrl+D）再改，或按第 1 节从零新建。
改坏了用 Undo，或从版本控制恢复那个 `.asset` 文件。

## 10. 完整字段参考

### 10.1 结构级（JigsawStructureFeatureDefinition）

**Identity and Materials**

| 字段 | 说明 |
|---|---|
| Enabled | 关闭则完全不生成 |
| Stable Id | 稳定 ID，参与缓存键 |
| Primary Voxel Type | 主体体素，必须非 Air |
| Accent Voxel Type | 装饰体素，留空回退为 Primary |

**Placement**

| 字段 | 说明 |
|---|---|
| Placement Strategy | `RandomSpread` 或 `ConcentricRings` |
| Seed Salt | 该结构独立随机盐，务必与其他结构不同 |
| Region Size In Chunks | 候选区域边长（区块），仅 RandomSpread |
| Placement Chance | 候选区域成立概率，仅 RandomSpread |
| Min / Max Floor Height | 起始模块地板高度范围 |

**Concentric Rings**（仅该策略）

| 字段 | 说明 |
|---|---|
| Ring Structure Count | 全世界候选总数 |
| Ring Count | 环数 |
| Ring Distance In Chunks | 每环半径步长 |
| Ring Spread In Chunks | 环内径向抖动 |

**Structure Set**

| 字段 | 说明 |
|---|---|
| Structure Set Id | 相同 ID 的结构竞争同一候选格；留空则不竞争 |
| Structure Set Weight | 竞争权重 |

**Piece Graph**

| 字段 | 说明 |
|---|---|
| Max Pieces | 整座结构 piece 上限（含起始与封口） |
| Max Depth | connector 图最大深度，起始为 0 |
| Max Horizontal Distance | piece 相对起点的最大水平距离 |
| First Piece Id | 可选，深度 1 的强制模块 |

**Layout Quality and Performance**

| 字段 | 说明 |
|---|---|
| Layout Attempts | 为满足 Minimum Count 的整图重试次数 |
| Connector Placement Attempts | 单个 socket 尝试不同候选的上限 |
| Collision Padding | 无父子关系 piece 之间的额外水平间隙 |

### 10.2 模块级（JigsawPieceDefinition）

**Identity and Pool**

| 字段 | 说明 |
|---|---|
| Stable Id | 结构内唯一 |
| Display Name | 编辑器显示名 |
| Pool Id | 本模块属于哪个池（能被谁抽中） |
| Output Pool Id | 无显式 socket 时的旧式出口目标池 |
| Start Piece | 是否起始模块，每个结构恰好一个 |
| Weight | 同池同深度的相对权重 |
| Minimum / Maximum Graph Depth | 允许出现的闭区间深度 |

**Selection Constraints**

见 2.1 节。

**Geometry and Behaviour**

| 字段 | 说明 |
|---|---|
| Shape | `Room` / `Corridor` / `Crossing` / `Stairs` |
| Build Style | `Excavated` / `Masonry` |
| Connector Pattern | 仅在 Explicit Sockets 为空时生效的兼容模式 |
| Decoration | 见 2.5 节 |

**Optional Voxel Template**

| 字段 | 说明 |
|---|---|
| Voxel Template | 指定后替代所有程序化尺寸 |
| Template Writes Air | 是否写入模板中的空气样本 |

**Box Dimensions**（Room / Crossing）

宽 / 深 / 高的最小最大值。宽深会被规范为奇数。

**Passage Dimensions**（Corridor / Stairs）

| 字段 | 说明 |
|---|---|
| Minimum / Maximum Length | 通道长度范围 |
| Width / Height | 通道宽高，宽规范为奇数 |
| Vertical Delta | 楼梯升降高度 |

**Outgoing Connections**

| 字段 | 说明 |
|---|---|
| Side Branch Chance | 兼容模式下的侧分支概率 |
| Descending Chance | 楼梯向下的概率 |
| Decoration Spacing | 装饰间距（2~12） |

### 10.3 Socket 级（JigsawConnectorDefinition）

| 字段 | 说明 |
|---|---|
| Stable Id | 模块内唯一 |
| Role | `Input` / `Output` / `Bidirectional` |
| Face | `Forward` / `Right` / `Back` / `Left`，相对模块朝向，旋转后自动换算 |
| Joint | 预留，当前用 `Aligned` |
| Socket Name | 自身名字 |
| Target Name | 期望对方的名字，`*` 通配 |
| Target Pool Id | 该出口的主候选池 |
| Fallback Pool Id | 主池全失败后尝试的封口池 |
| Along Offset | Passage 侧面 socket 沿通道的偏移，`-1` 用中点 |
| Lateral Offset | 前后墙接口相对中心的横向偏移 |
| Vertical Offset | 开口底部相对地板的高度 |
| Activation Chance | 该出口进入生长队列的概率 |
| Opening Width / Height | 实际雕出的门洞尺寸 |

### 10.4 Spawn Marker 级（StructureSpawnMarkerDefinition）

见第 4 节。可配置在模块上，也可配置在体素模板上（模块未配置时继承）。

### 10.5 体素类型的 Group

新增体素类型（`Create > Supernova > Voxels > Voxel Type Definition`）时，
除了 `Type`（唯一 ushort ID）、`Display Name`、`Durability`、`Material`，
还要设 **Group**：

| Group | 含义 |
|---|---|
| `Structure` | 建造几何：Default、StructureBrick、FortressBrick |
| `Stone` | 天然岩石：Stone、Bedrock |
| `Ore` | 可采矿脉：Ore |

**同一 Group 内的体素会生成连贯的网格**，不同 Group 之间才留分界。
结构用的所有砖类务必设为 `Structure`，否则墙上会出现坑洼接缝。

新建后记得把它加进 `Assets/Game/Config/MinecraftVoxelTypes.asset` 的
`Definitions` 列表，并确认 `Type` ID 与现有类型不重复。

## 11. 常见问题

**Q：结构生成了，但走廊分支明明有门洞，门后却是墙。**
A：Fallback 池没配。见 2.2 节配一个封口模块。

**Q：两个不同结构长在了一起 / 位置重叠。**
A：`Seed Salt` 相同。给每个结构一个独立的盐值。

**Q：必达房间经常不出现。**
A：见 8.3 节。优先调大 `Layout Attempts` 和 `Max Horizontal Distance`。

**Q：结构悬空在洞穴里，下面没有支撑。**
A：给大模块加 `SupportToGround` 处理器，见第 3 节。

**Q：改了资产但生成结果没变。**
A：布局按内容哈希缓存，改任何字段都会自动失效。如果确实没变，检查是否
改到了正在被世界配置引用的那份资产，以及 `Enabled` 是否勾选。

**Q：结构墙面有坑洼 / 接缝。**
A：结构用的两种砖不在同一个 Group。见 10.5 节。

**Q：想让某个房间只能由楼梯接入。**
A：给楼梯出口和该房间入口用一组专属名字（如 `ruin_vertical` /
`ruin_vertical_entry`），其他模块都不用这组名字。

**Q：一个模块能有几个入口吗？**
A：可以。多个 Input socket 时，生成器会在所有兼容入口中等概率随机选一个
对齐，于是同一个房间可能从不同方向被接入。

**Q：想让某个房间必定有一只 Boss。**
A：在该模块加一个 `Kind = Monster`、`Spawn Chance = 1` 的 spawn marker，
见第 4 节。注意 Boss 房本身也要 `Minimum Count = 1`（见 2.1 节），否则
房间本身都不一定出现。

**Q：宝箱嵌在墙里 / 浮在空中。**
A：勾上 marker 的 `Snap To Floor` 并给足 `Floor Search Distance`（6 左右）。
见 4.2 节。

**Q：结构里的怪物没出现。**
A：三种可能：`Maximum Marker Monsters` 已满；marker 的 prefab 没填（校验会
报 Error）；`Snap To Floor` 找不到地面而放弃。检查 Inspector 的校验提示。
