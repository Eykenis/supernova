# Vintage Story 与 7 Days to Die 体素支撑系统实现深挖

## 1. 调研基线

本文是《体素应力系统调研.md》的展开，聚焦两款游戏的体素支撑/结构完整性
系统的**算法级实现细节**。调研时间 2026-08-06，通过抓取源码与社区分析获得。

**重要来源强度声明**：

- **Vintage Story**：行为代码在开源仓库 `anegostudios/vssurvivalmod`，
  本次**抓到了完整源码**，算法级细节可直接引用，置信度高。
- **7 Days to Die**：游戏源码不公开，所有算法信息来自社区反向工程 + 官方
  wiki，**无法逐行验证**。凡社区结论均标注为社区反向工程，可靠度低于源码级。

核对材料（完整列表见 §6）：

- [vssurvivalmod/BlockBehaviorUnstableRock.cs](https://github.com/anegostudios/vssurvivalmod/blob/master/BlockBehavior/BlockBehaviorUnstableRock.cs)
- [vssurvivalmod/BehaviorUnstableFalling.cs](https://github.com/anegostudios/vssurvivalmod/blob/master/BlockBehavior/BehaviorUnstableFalling.cs)
- [vssurvivalmod/Systems/SupportBeams/](https://github.com/anegostudios/vssurvivalmod/tree/master/Systems/SupportBeams)
- [7DTD Wiki — Structural Integrity](https://7daystodie.fandom.com/wiki/Structural_Integrity)
- [7DTD Wiki — Max Load](https://7daystodie.fandom.com/wiki/Max_Load)

## 2. Vintage Story — 源码级分析

VS 有两套独立的"不稳定块"行为，**触发条件完全不同**：

1. `BlockBehaviorUnstableRock` —— 洞穴/岩体塌方（cave-ins），依赖支撑距离。
2. `BlockBehaviorUnstableFalling` —— 重力坠落（沙/砾石），只查正下方。

两者最终都生成 `EntityBlockFalling` 实体。

### 2.1 垂直支撑强度 `getVerticalSupportStrength`（源码）

不是"正下方 4 格必须全实心"，而是**从下往上扫、遇到第一个空隙就归零**：

```text
getVerticalSupportStrength(world, pos):
    for i = 1..4:                        # 检查下方最多 4 格
        block = GetBlock(pos.Y - i)
        stab = block["unstableRockStabilization"]  # 特殊稳定方块属性，默认 0
        if stab > 0: return stab          # 稳定方块（柱子/锚点）返回其稳定值
        if 该格 UP 面或 DOWN 面不 solid: return 0   # 出现空隙 → 无垂直支撑
    return 1                              # 4 格全上下实心 → 垂直稳定
```

比概要想的更苛刻：正下方**不能有任何空洞**，否则判定无垂直支撑。

### 2.2 水平支撑搜索 `getNearestVerticalSupports`（源码）

BFS 沿 4 个水平方向找支撑点，**稳定不能穿过空气传播**：

```text
若 startpos 自身垂直稳定 → 直接返回（自身就是支撑）
否则默认 Unconnected = true

BFS 水平邻居 npos:
    若 npos 的搜索方向前后面不 solid → 跳过（支撑不能穿过空气）
    若 distSq > 6*6 → 不扩展，且若 npos 下方不 solid 则保持 Unconnected
    若 npos 垂直稳定 → Unconnected = false，记为支撑点，不扩展
    否则入队继续
```

支撑点存为 `Vec4i(npos, str)`，`str` = 支撑强度（1 或 `unstableRockStabilization`
值），存在 W 分量。

### 2.3 支撑距离换算 `searchCollapsible`（源码）

```text
NearestSupportDistance = 9999
for 每个支撑点 pos:
    NearestSupportDistance = min(NearestSupportDistance,
        sqrt(max(0, pos.HorDistanceSqTo(pos.X, pos.Z) - (pos.W - 1))))
    # pos.W 是支撑强度，(W-1) 被从水平距离平方中减去
    # → 支撑强度越高，等效距离越短，更结实支撑延长有效距离

beamDist = ModSystemSupportBeamPlacer.GetStableMostBeam(startPos, ...)  # 支撑梁
NearestSupportDistance = min(NearestSupportDistance, beamDist)

Instability = clamp(NearestSupportDistance / maxSupportDistance, 0, 99)
```

**默认 `maxSupportDistance = 2`**（格）。支撑梁作为**独立支撑源**参与 min()。

### 2.4 坍塌判定 `CheckCollapsible`（源码）——两级门槛

```text
if Unconnected:  collapse(...)              # 无支撑块：无条件坍塌
else:
    if world.Rand.NextDouble() + 0.001 > Instability: return   # 第一级：距离比
    if world.Rand.NextDouble() > collapseChance: return        # 第二级：25% 概率
    collapse(...)
```

**两级门槛语义**：

- `Instability < 1`（支撑距离 < 2 格）时，`NextDouble()+0.001 > Instability`
  几乎总为真 → **通常不塌**。
- `Instability ≥ 1`（支撑距离 ≥ maxSupportDistance）时，才大概率进入
  **第二级 25% 概率抽签**（`collapseChance = 0.25`）。
- 无支撑块（Unconnected）**无条件塌**，不受概率影响。

### 2.5 塌方级联 `getNearestUnstableBlocks`（源码）

级联不是确定性逐块传播，而是**有界 BFS + 抽样式二次触发**：

```text
blocksToCollapse = 2 + Rand.Next(30) + Rand.Next(11)*Rand.Next(11)  # 2..~152 块
maxy = 1 + Rand.Next(3)                                              # 向上 1..3 格

BFS 从 startPos：
    邻居 npos:
        distSq > 12*12 → 跳过（水平 12 格有界）
        npos.Y - startPos.Y >= maxy → 跳过（只向上有限格）
        npos 无 UnstableRock 行为 → 跳过
        dist > 0 → 加入 unstableBlocks
            # 额外把正下方 1..3 格"无垂直支撑"的不稳岩块也加入
            # 超过 blocksToCollapse 上限 → 返回
```

**级联触发机制**：

- `collapseLayer()` 按 **Y 从低到高**逐层坍塌，每层间隔 **200ms**
  （`RegisterCallback(..., 200)`）。
- 每块坍塌前检查是否已有 `EntityBlockFalling` 在同一坐标（防重复）。
- 坍塌后，**随机在附近 8 格半径内抽查 3 次** `checkCollapsibleNeighbours`
  （每次抽 3 个随机面方向，遇到第一个可塌的就 break）——这是"级联"
  的真正来源：**抽样式二次触发，不是确定性链式传播**。
- 触发入口：`OnBlockBroken`、`OnBlockExploded`、`DidPlaceBlock` 都检查；
  `ModSystemExplosionAffectedStability` 在爆炸事件里对爆炸半径内块随机采样。

### 2.6 支撑梁系统（全新发现，概要未提）

源码位于 `Systems/SupportBeams/`：`BlockSupportBeam.cs`、`PlacedBeam.cs`、
`BEBehaviorSupportBeam.cs`、`ModSystemSupportBeamPlacer.cs`。

**玩法**：支撑梁不是整块方块，玩家**画一条 A→B 梁线**（右击设起终点，
Ctrl 切换 4×4 或 16×16 网格吸附），按块计消耗物品，梁会"下垂"
（`SlumpPerMeter`）。

**支撑原理（源码）**：

```text
GetStableMostBeam(blockpos):
    mostlyVertical = (len * 1.5 < |End.Y - Start.Y|)   # 是否"大致垂直"梁
    stable = mostlyVertical ? (stableAt(Start) || stableAt(End))   # 垂直梁只要求一端有支撑
                           : (stableAt(Start) && stableAt(End))    # 水平梁必须两端都有支撑
    dist = DistanceToLine(point, Start, End)          # 点到梁的垂距
    return 所有 stable 梁中最小的垂距

isBeamStableAt(p) = getVerticalSupportStrength(p) > 0   # 复用 UnstableRock 静态方法
```

- 每条梁登记进所在 chunk 的 `LiveModData["supportbeams"]`，解决梁跨 chunk
  的查找问题（每端各自登记）。
- **垂直梁只要求一端有支撑；水平梁必须两端都有支撑。**
- 作用：把块"接到"梁上，`NearestSupportDistance = min(支撑点距离, beamDist)`
  用梁的垂距替代支撑距离 → 等效扩展悬挑范围。**不是简单地调大
  maxSupportDistance**，而是独立的线段几何支撑源。

### 2.7 坠落行为 `BehaviorUnstableFalling`（源码）

- 触发：`OnBlockPlaced` 和 `OnNeighbourBlockChange` 调 `TryFalling`。
- 条件：下方块 `Replaceable > 6000`（空气/水/可替换物）；`fallSideways`
  开启时额外 30% 概率检查侧向。
- **延迟一帧执行**（源码注释明确）：因为 `EntityBlockFalling` 落地时先
  `SetBlock` 再 `FromTreeAttributes`，若在放置回调内同步生成会让刚生成的
  BE 数据不全。
- 参数默认：`fallSideways=false`、`fallSidewaysChance=0.3`、
  `dustIntensity=0`、`impactDamageMul=1`。

### 2.8 坠落物理 `EntityBlockFalling`（部分源码 + 推断）

- **源码注释确认**：落地时调用 `SetBlock` 把方块**写回体素**（不是保持
  实体），再恢复 BE 数据。受击实体伤害 = 18 × 坠落距离 × `impactDamageMul`。
- **物理驱动**：`EntityBehaviorPassivePhysics`（vsapi 仓库），核心公式：

  ```text
  gravityStrength = (GravityPerSecond/60 * dtFactor) + max(0, -0.015 * Motion.Y * dtFactor)
  Motion.Y -= gravityStrength
  ApplyTerrainCollision(...)   # 每轴地形碰撞
  ```

- ⚠️ **诚实缺口**：`EntityBlockFalling` 类**本体**的落地转换条件源码未抓到
  （核心 DLL 私有，GitHub 只有 mod 层）。"落在哪、落在体素上还是实体"
  的边界条件为**推断**：从每轴碰撞 + 一帧延迟注释看，是"落到体素上时
  SetBlock 写回"。

### 2.9 世界配置路径（源码）

- `CaveIns = World.Config.GetString("caveIns") == "on"`（字符串 "on"）。
- `AllowFallingBlocks = World.Config.GetBool("allowFallingBlocks")`。
- `Enabled = CaveIns && AllowFallingBlocks`（**两者都要开**）。
- 通过 `/worldconfig` 命令设置，不是 JSON 文件路径。JSON 只控制单方块行为
  属性（`collapseChance`、`maxSupportDistance`、`unstableRockStabilization`、
  `slumpPerMeter` 等）。
- `unstableRockStabilization` 读 `block.Attributes["unstableRockStabilization"]`，
  默认 0，只有设置了该属性的方块才提供额外稳定。

## 3. 7 Days to Die — 社区反向工程分析

⚠️ 前置：7DTD 源码不公开，本节全部来自社区反向工程 + 官方 wiki，无法逐行
验证。**所有数值有版本漂移**，需以当前 alpha 版游戏内为准。

### 3.1 核心公式与传播方向

```text
Structural Integrity = Max Load ÷ Mass（向下取整）
```

- 即一块水平悬挑的材料能支撑多少个**同材料**块。
- **载荷传播方向（社区结论，较可靠）**：**从支撑点（垂直支撑列）向外的
  水平传播**。每个块"拥有"一个可用支撑量（max load），向一个方向传播时
  逐块递减其质量；累积质量超过可用支撑 → 该段坍塌。
- **垂直方向**：只要有一条不中断的块链直通 bedrock/地面，就是无限支撑；
  任何空隙都会断链并重置该列的可用支撑。
- 社区帖提及载荷计算"从 5×5 的地面支撑区域开始"，悬挑超出该区域才按水平
  载荷算——**【推断】**，未经官方确认。

### 3.2 垂直支撑 vs 水平支撑的实现区分

- 代码层无公开源码，但**行为层区分明确**（多来源一致）：**垂直支撑 =
  无限稳定，永不计载荷**；水平悬挑 = 按 Max Load/Mass 计数。
- **"垂直支撑永远稳定"是核心规则**，不是通过"载荷很大"实现——垂直路径
  根本**不计入载荷**。
- `Max Load` = 该块水平方向能承受的总质量；`Mass` = 该块自身贡献的质量。
- **7DTD 是纯载荷计数**，没有"支撑距离 + 载荷计数"的组合——支撑距离由
  "每块质量累加"隐式体现（块越多质量越大，直到超过 Max Load）。

### 3.3 非支撑块行为

- 无垂直支撑且水平载荷超限 → **坍塌**。坍塌不是立即变掉落物，而是
  **结构整体坍塌级联**：上方失去支撑的块一起落下，**落下产生的废墟
  （rubble）本身算质量**，砸到下一层再超载，形成向下传播的级联直到地面。
- 这与 VS 的"抽样式二次触发"完全不同——7DTD 是"载荷超限 → 一次大规模坍塌"。

### 3.4 塌方级联机制（社区确认存在）

- **有级联**，但机制与 VS 不同：不是逐块概率，而是**载荷重新计算**触发。
  游戏在 chunk 加载、挖矿、加/删块、植物生长（变重）时重算稳定；重算发现
  某块超载就整片塌，塌方废墟再压垮下层。
- **预置 POI 建筑有特殊标记/隐藏 bedrock 块，忽略稳定计算**（所以搜刮 POI
  不会塌，自己挖空地基才会）——【社区推断，可靠度较高】。

### 3.5 数值表（多来源，标注版本差异）

| 材料 | Max Load | Mass | SI（支撑块数） | 来源 |
|---|---|---|---|---|
| 木材 Frame/Wood | 40 | 5 | 8 | fandom SI 页 + 社区 |
| 鹅卵石 Cobblestone | 120 | 10 | 12 | fandom SI 页 |
| 混凝土 Concrete | 120 | 10 | 12 | fandom SI 页（多来源一致） |
| 钢 Steel | 300~320 | 20 | 15~16 | fandom：钢 300/20，reinforced 320/20 |
| 铁条/Iron Bars | 300 | 20 | 15 | fandom |
| Rebar 框架 | 320 | 20 | 16 | 社区 |
| Poured Form（浇筑模板） | 24 | 5 | ~4 | 社区（较弱，升级反而会塌） |

**重点差异**：

- **"15 块硬上限"**：多来源提到任何材料都无法超过 15 块水平悬挑，第 16 块
  必掉（游戏限制）。这与"钢 SI=16"冲突，实际被 cap 到 15。这是社区推断，
  与"8 块后踩上去会部分塌"的说法互证。⚠️ 上份报告里"15 块硬上限"表述曾
  被验证流程否决，此处作为社区说法保留、不当作确定规则。
- 版本间数值有变动（旧版 cement 120/10、rebar 320/5 等）。
- **升级会加重**：混凝土(10) 升级到钢(20) 增加质量，若该悬挑已满载荷会触发
  坍塌——官方 wiki 建议从支撑柱向外升级。

## 4. 两模型对比与对本项目的启示

| 维度 | Vintage Story | 7 Days to Die |
|---|---|---|
| 模型类型 | **局部的概率稳定性**（每块独立判定） | **全局的确定性载荷**（传播式） |
| 支撑判定 | 有界 BFS 找支撑 + 距离/比例梯度 | 从支撑点向外分配载荷 |
| 坍塌触发 | 距离比 → 25% 概率抽签 | 载荷超限 → 整体塌 |
| 级联 | 抽样式二次触发（12 格/152 块有界） | 废墟变载荷 → 向下传播 |
| 性能 | 友好（有界 BFS + 随机化） | 官方自认重算吃资源，事件触发时才算 |
| 垂直支撑 | 下方 4 格无空隙即稳 | 垂直路径不计载 = 无限稳 |
| 可预测性 | 低（概率）但涌现自然 | 高（确定） |

**对本项目（Unity + Marching Cubes 平滑洞穴）的启示**：

1. **VS 模型更适合原型**：有界 BFS + 随机化，代价可控（12 格/152 块上限），
   且"无支撑块无条件塌、近支撑块概率塌"两级语义简单清晰。
2. **7DTD 模型适合确定性建造玩法**：载荷累加可精确预测，但重算代价高、
   坍塌规模大；若要做"挖地基导致大楼整体塌方"的戏剧性玩法才值得。
3. **支撑梁是一个极好的"可玩扩展"**：VS 的线段梁系统独立于方块支撑，
   用 min() 合并进距离计算——本项目可用"玩家放置的支撑结构"作为第二支撑源。
4. **两个模型的共同点**：都**不做全地形连通性重算**（VS 用有界 BFS，
   7DTD 用载荷传播），都**事件驱动**（破坏/放置时触发），都对"垂直支撑"
   有特殊豁免。这与《体素应力系统调研》档位 (b)/(c) 完全吻合。

## 5. 诚实标注的缺口

1. `EntityBlockFalling` 精确落地逻辑（落地坐标、能否落水面/实体、SetBlock
   触发）——核心 DLL 私有，推断。
2. 7DTD 全部算法细节（载荷传播确切实现、5×5 支撑区、15 块硬上限判定）——
   无官方源码，社区反向工程。
3. 7DTD 数值表版本准确性——跨版本来源有冲突。
4. `getNearestUnstableBlocks` 里 `dist > 0` 与 `blocksToCollapse` 随机分布的
   边界行为，从代码推的，未见文档描述。

## 6. 来源

**Vintage Story 源码（GitHub，本次已抓全文）：**

- BlockBehaviorUnstableRock.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/BlockBehavior/BlockBehaviorUnstableRock.cs
- BehaviorUnstableFalling.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/BlockBehavior/BehaviorUnstableFalling.cs
- Systems/SupportBeams/BlockSupportBeam.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/SupportBeams/BlockSupportBeam.cs
- Systems/SupportBeams/BEBehaviorSupportBeam.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/SupportBeams/BEBehaviorSupportBeam.cs
- Systems/SupportBeams/ModSystemSupportBeamPlacer.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/SupportBeams/ModSystemSupportBeamPlacer.cs
- Systems/SupportBeams/PlacedBeam.cs：
  https://github.com/anegostudios/vssurvivalmod/blob/master/Systems/SupportBeams/PlacedBeam.cs
- vsapi BehaviorPassivePhysics.cs：
  https://github.com/anegostudios/vsapi/blob/master/Common/EntityBehavior/BehaviorPassivePhysics.cs

**Vintage Story 文档/社区：**

- BlockBehaviorUnstableRock JSON docs：
  https://apidocs.vintagestory.at/json-docs/jsondocs/Vintagestory.GameContent.BlockBehaviorUnstableRock.html
- BlockBehaviorUnstableFalling JSON docs：
  https://apidocs.vintagestory.at/json-docs/jsondocs/Vintagestory.GameContent.BlockBehaviorUnstableFalling.html
- 1.20.4 坠落消失 bug：https://github.com/anegostudios/VintageStory-Issues/issues/5308

**7 Days to Die（社区/wiki，无官方源码）：**

- Fandom Structural Integrity：
  https://7daystodie.fandom.com/wiki/Structural_Integrity
- Fandom Max Load：https://7daystodie.fandom.com/wiki/Max_Load
- wiki.gg Mass：https://7daystodie.wiki.gg/wiki/Mass
- Steam 讨论（SI 算法）：
  https://steamcommunity.com/app/251570/discussions/0/35222218880053688/
  https://steamcommunity.com/app/251570/discussions/4/2646360821010527884
  https://steamcommunity.com/app/251570/discussions/4/3196991412354006220/
