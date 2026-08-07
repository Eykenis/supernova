# Teardown 连通性检测算法调研

## 1. 调研基线

本文调研游戏 Teardown（Tuxedo Labs / Saber Interactive）的可破坏体素
物理中的"连通分量检测"（connected component detection）——即破坏发生后，
如何判断哪些体素从主体脱离并成为新的独立刚体。调研时间为 2026-08-06，
基于定向抓取的一手来源（开发者访谈、官方技术博客）与逆向工程分析。

核对材料：

- [SED1772 完整转录（Software Engineering Daily，2025-01-02）](http://softwareengineeringdaily.com/wp-content/uploads/2024/12/SED1772-Teardown.txt)
- [80.lv 开发者访谈](https://80.lv/articles/teardown-developer-breaks-down-multiplayer-and-voxel-destruction-tech)
- [Voxagon 官方博客《The unlikely story of Teardown Multiplayer》](https://blog.voxagon.se/2026/03/13/teardown-multiplayer.html)（镜像：[fooqux.com/article/3560](https://fooqux.com/article/3560)）
- [acko.net《Teardown Frame Teardown》（逆向）](https://web.archive.org/web/20250306142148/https://acko.net/blog/teardown-frame-teardown/)
- [juandiegomontoya《Teardown Teardown》（逆向）](https://juandiegomontoya.github.io/teardown_breakdown.html)
- [Teardown Wikipedia](https://en.wikipedia.org/wiki/Teardown_(video_game))

## 2. 重要声明：算法实现细节未公开

**首先必须诚实说明：** Teardown 开发者 Dennis Gustafsson 从未公开披露
连通性检测用的是 flood fill / BFS / union-find 中的哪一种，也未说明是
增量更新还是每帧全算。本文所有"有来源支撑的事实"与"工程推断"严格分开
标注。任何声称"Teardown 用 union-find"的二手博客均无一手依据，**不要
采信**。

## 3. 有来源支撑的事实

### 3.1 事件驱动，非每帧

破坏是事件驱动：破坏命令流（"cut hole in this shape at voxel coord
x,y,z" / "change ownership of that shape" / "reconnect joint to this
shape"）在破坏发生时产生，而非每帧（Voxagon 官方博客）。连通性检测
作为破坏命令的后继，是"破坏后按需触发"，不是每帧热点。

### 3.2 EA 首发时功能受限，数月后才补全

- 2020 EA 首发时，"find connected parts"（找连通部分）**只适用于较小
  的物体**。对大房子，玩家拆掉整层甚至只剩最后一格体素，房子仍立着，
  引发玩家强烈不满（SED1772，时间戳 [0:22:48]）。
- 该功能是**首发后数月**才补上的："That's something we updated a few
  months later"（[0:22:48]）。

> 关键启示：**连通性检测是业界公认难做的部分，连 Teardown 都是发售后才
> 补齐的。** 做可破坏体素时，应把连通性当作一个后期模块而非首版功能。

### 3.3 核心难点：规模

Gustafsson 明确说算法必须能处理"无限大物体"（mod 出来的任意大结构），
最坏情况是**搜索整个关卡——上亿体素**（"you may end up searching the
whole level, which is hundreds of millions of voxels"）([0:23:37])。

这句话是对最坏情况的描述，说明他们没有（至少不依赖）纯局部技巧——
当时的难点就是全局搜索的规模。但这也间接说明**他们做了某种裁剪或优化**
（见 §5），因为破坏命令被设计为"与物体大小无关"（"commands are the
same regardless of object size"，Voxagon 博客）。

### 3.4 天然按物体分体积

游戏用"**数千个较小的体素体积**"而非一个巨型全局体积
（80.lv / gamedeveloper.com / Wikipedia）。这是连通性计算的**天然边界**：
连通性搜索可以被限制在单个物体（单个体积）内部，而不是整个关卡。

### 3.5 碎块确实成为独立刚体（证据最硬）

- 破坏命令流包含 "change ownership of that shape"、"reconnect joint
  to this shape"——分离的碎块成为拥有独立 shape/body、且关节会被重连的
  独立刚体（Voxagon 官方博客）。
- acko.net 确认："当物体被炸开时，引擎会把它们**分离成不连通的块，并
  为每一块新建一个独立对象**。这个过程可以无限重复。"
- 刚体质量属性是**每个 chunk 一个"惯性盒"（inertia box）**，方向由其
  惯性张量决定；chunk 被修改（=被破坏/分离）时**重新计算**。

> 注：惯性盒/惯性张量细节的唯一出处是一篇匿名帖（nancygold），可靠度
> 中低，标注为二手信息。但 acko.net 独立验证了"调试框不是 collider"，
> 与惯性盒说法方向一致。

### 3.6 没有传统 collider

- 碰撞是**体素对体素、CPU 上直接算**，不是 PhysX/MeshCollider
  （SED / 80.lv / gamedeveloper 三方印证）。
- 调试框是惯性盒可视化，**不是 collider**，甚至不完全包住物体。
- 渲染为每物体 OBB + 逐体素 GPU ray march（acko.net / juandiegomontoya），
  因此碎块成为新刚体时**不需要重建三角形碰撞网格**——物理形状就是体素
  集本身，渲染也只需把碎块作为一个新 shape 加入体积纹理。

## 4. 工程推断（无一手来源，供参考）

以下为基于事实的合理推断，**非 Teardown 事实**，落地时需自行验证：

1. **最可能的算法形态**：以体素为节点的 BFS/洪泛（flood fill），从某个
   锚定体素出发沿 6 邻接（或 26 邻接）扩展，标出仍连通的集合，未被标记
   的就是分离碎块。
2. **局部化裁剪**：鉴于"搜索整个关卡会很慢"是公开痛点，工程上几乎必然
   做了裁剪——如从破坏点邻域出发、或把大物体预分块并记录块间连通表。
3. **union-find（并查集）** 是社区/通用实现里被反复推荐的方案，但**没有
   任何来源说 Teardown 用它**。

## 5. 通用业界做法（非 Teardown 专属）

当 Teardown 专属细节不可得时，以下社区公认方案可作落地参考。

### 5.1 单体素移除后的局部 flood fill（最省事的基线）

被社区反复推荐的基线做法——移除一个体素后，只对其**至多 6 个实心邻居**
各自做 BFS 扩展：

```text
remove(voxel v):
    for each solid neighbor n of v:      # 至多 6 个
        region[n] = new region {n}
    expand each region BFS（只走实心邻接）
    if all regions merged -> 无脱离
    else -> 每块独立 region 成为候选碎块
```

### 5.2 union-find 维护邻接簇

用并查集维护体素簇，可把重计算**延后到显式 split/merge 指令时**——与
Teardown 的破坏命令流思路一致。避免每次破坏都全量重算。

### 5.3 分块 + 块间连通表

把更新局部化到被改动的 chunk：记录块间的连通关系，破坏只更新受影响
块的连通表，而不是对整个物体重算。这是"局部化"思想的一般化。

### 5.4 碎块碰撞体的生成（给 Unity 项目）

Unity 项目无法像 Teardown 一样用体素做碰撞，碎块仍需要 collider：

- **体素点云建凸包**：断连块 = 体素位置点云 → 凸包（gift-wrapping /
  QuickHull）作为刚体 collider；或用 PCA 主轴 + 中位数切割递归拆成
  多个凸包近似非凸形状（Cubiquity 论坛帖）。
- **只搜表面体素**：检测只搜索表面体素，把问题从 O(n³) 降到 O(n²)。
- 物理引擎原生支持：Rapier 0.17+ 支持动态刚体挂 voxel collider、逐体素
  增删，但**不自动算质量/角惯量**（需自己算——正合 Teardown"惯性盒重算"
  的思路）。

### 5.5 Minecraft 沙/砾石范式（不适用于物体倒塌）

Minecraft 的沙/砾石**不做连通性**，只查**正下方一格**是否支持（是
air/液体则下落），由 block update 触发。结论：**Minecraft 范式不适用于
Teardown 式物体倒塌**，只适合单格重力。

## 6. 与本研究项目的对应建议

本项目（Unity + Marching Cubes 平滑体素）若要做"破坏后脱离块倒塌"：

1. **不要首版就做精确连通性**。Teardown 都是 EA 后补的。首版用
   §5.1 的局部 flood fill 或更简单的"无支撑块按概率下落"（Vintage Story
   的 `collapseChance` 方案，见《体素应力系统调研》）即可。
2. **按物体/结构边界切分连通性域**，避免全局重算——本项目结构体
   （Jigsaw）天然是连通域边界。
3. **破坏事件驱动检测**，不在每帧跑。
4. **碎块刚体**：本项目已有 `BreakFragmentEffect` + `MeshFragmentBuilder`
   的完整碎片链路，分离块可直接复用（但那是纯视觉碎片；若要"脱离块作为
   可交互刚体"，需按 §5.4 生成碰撞体 + 复用 `MinedOreDrop`/`ValuableObject`
   的价值与磁吸链路）。
5. **Unity 的折中**：破坏后做 BFS 标记"与大地不连通"的体素簇，簇体积
   超过阈值（如 8 体素）才生成独立刚体，避免每个碎块都实例化。

## 7. 来源

**一手（开发者/官方）：**

- SED1772 完整转录（Dennis Gustafsson 访谈）：
  http://softwareengineeringdaily.com/wp-content/uploads/2024/12/SED1772-Teardown.txt
  （episode 页：
  https://softwareengineeringdaily.com/2025/01/02/teardown-and-voxel-based-rendering-with-dennis-gustafsson/）
- 80.lv 开发者访谈：
  https://80.lv/articles/teardown-developer-breaks-down-multiplayer-and-voxel-destruction-tech
- Voxagon 官方博客《Teardown Multiplayer》：
  https://blog.voxagon.se/2026/03/13/teardown-multiplayer.html
  （镜像：https://fooqux.com/article/3560）
- gamedeveloper.com：
  https://www.gamedeveloper.com/design/how-beautiful-voxels-laid-the-way-for-i-teardown-s-i-heist-y-framework

**逆向/分析：**

- acko.net《Teardown Frame Teardown》：
  https://web.archive.org/web/20250306142148/https://acko.net/blog/teardown-frame-teardown/
- juandiegomontoya《Teardown Teardown》：
  https://juandiegomontoya.github.io/teardown_breakdown.html
- nancygold 笔记（弱来源，惯性盒细节）：
  http://lj.rossia.org/users/nancygold/148707.html

**通用/社区做法：**

- Stack Overflow 断连检测：
  https://stackoverflow.com/questions/73479966/destroy-disconnected-parts-from-main-part
  https://stackoverflow.com/questions/58676100/detecting-if-voxel-or-voxel-group-is-still-connected-to-rest-of-object
- GameDev.net union-find 讨论：
  https://gamedev.net/forums/topic/714520-determine-if-two-voxels-are-in-the-same-assembly/
- Cubiquity（碎块凸包碰撞）：
  https://discussions.unity.com/t/cubiquity-a-fast-and-powerful-voxel-plugin-for-unity3d/506277/608
- Minecraft 沙/砾石：
  https://minecraft.wiki/w/Block_update
