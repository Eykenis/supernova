# 地图生成分层与 Pass 修正

下面这套理解可以作为高层模型，但需要修正三个关键点。

## 1. 数据层尺寸需要区分三种概念

| 概念 | 尺寸 | 用途 |
|---|---:|---|
| 体素柱数据容器 | 32×256×32 | InfiniteVoxelWorld 的实际存储、流送单位 |
| Dense 世界有效区域 | 32×64×32 | 当前 Dense 配置只生成和使用 Y=0～63 |
| Mesh Section | 32×32×32 | 单次 Marching Cubes 构网任务 |

因此，更准确的说法是：

> 世界是理论无限的体素网格，以 X/Z 索引的 32×256×32 体素柱为生成、流送和更新单位；当前 Dense 世界每柱只有 32×64×32 的有效内容，并进一步拆成两个 32³ Section 构建 Mesh。

注意，“整个世界”并不会同时存在于内存里，只保存玩家附近和保留范围内的体素柱。

## 2. Marching Cubes 不是“查询”，而是表面提取

表现层更准确的描述是：

> Marching Cubes 从体素密度和类型数据中提取等值面三角网格，再生成渲染 Mesh、材质 SubMesh、MeshCollider 和洞穴表面物件。

每个 32³ Section 构网时，会捕获 33³ 样本，额外的一层用于读取正方向邻接边界，保证相邻 Section 的表面连续。

Marching Cubes 的输入是：

\[
(\rho(x,y,z),T(x,y,z))
\]

实体判定是：

\[
\rho\ge \rho_{\mathrm{iso}}
\quad\land\quad
T\neq Air
\]

输出才是顶点、三角形和 SubMesh。

“查询”更多发生在游戏交互阶段，例如射线检测、体素寻址和采矿目标解析，而不是 Mesh 生成阶段。

## 3. 四个 PASS 可以成立，但不是代码中的四个正式状态

代码中的正式初始状态是：

\[
Terrain\rightarrow Structures\rightarrow Meshes\rightarrow Ready
\]

四段模型可以调整成下面这样：

~~~mermaid
flowchart LR
    A["Pass 1：体素柱生成"] --> B["Pass 2：全局结构处理"]
    B --> C["Pass 3：Mesh 与 Collider 构建"]
    C --> D["Pass 4：物件与玩法内容实例化"]
~~~

### Pass 1：体素柱生成

这个阶段不只是“自然地形”，实际顺序是：

1. 洞穴密度场或 Superflat 数据。
2. 根据密度确定 Stone/Air。
3. 上下边界 Bedrock。
4. 矿物 Feature。
5. Jigsaw 结构 Feature。
6. 其他结构 Feature。
7. 清理有效高度以上的数据并恢复 Bedrock。

也就是说，矿物、Jigsaw 等“结构化内容”有一部分已经在第一阶段按柱写入体素数组。

### Pass 2：全局结构处理

等初始所需体素柱全部生成后，再处理不能独立按柱完成的内容：

- 出生点固定体素结构。
- 降落舱或出生区域净空。
- 地面支撑和头顶空间处理。
- 边界 Bedrock 恢复。
- Dense 外置降落舱的特殊分支。

这个阶段对应正式状态中的 Structures。

### Pass 3：Mesh 与物理构建

每个有效 Section 依次执行：

1. 捕获 33³ 体素样本。
2. 后台运行 Marching Cubes。
3. 主线程创建渲染 Mesh 和材质 SubMesh。
4. 延迟更新 MeshCollider。
5. 构建表面层和表面装饰。
6. Physics.SyncTransforms。
7. 将对应柱标记为物理就绪。

### Pass 4：物件生成

这可以作为概念上的第四 Pass，但代码中没有统一叫作 Objects 的正式状态。

物件会在不同的就绪时机生成：

- Treasures：对应柱 Mesh/Collider 就绪后生成。
- 结构 Marker：对应柱物理就绪后实例化。
- Checkpoint：对应柱物理就绪后实例化。
- 表面植被或装饰：Mesh 后处理阶段生成。
- Monsters：初始或运行时生成逻辑单独调度；Dense 世界还会等玩家穿过传送门后才开启自然生成。
- 矿物掉落：玩家采矿时动态产生，不属于初始世界生成。

## 更准确的整体表述

> **数据层**：理论无限体素网格，以 32×256×32 的 X/Z 体素柱进行生成和流送；当前 Dense 世界每柱有效高度为 64，并按 32³ Section 更新 Mesh。  
> **生成层**：先生成每柱密度、类型、矿物和按柱结构，再执行需要全局上下文的出生区与固定结构处理。  
> **表现与物理层**：用 Marching Cubes 将体素密度场提取成三角网格，并分阶段生成渲染 Mesh、材质、MeshCollider 和表面内容。  
> **玩法物件层**：等对应柱的 Mesh 和物理状态就绪后，再生成 Treasure、Checkpoint、结构 Marker 等场景对象；Monster 等系统另行调度。

主要修正是：**32×64×32 是当前 Dense 世界的有效柱尺寸，不是底层通用容器尺寸；结构写入跨越前两个阶段；物件生成也不是统一的正式第四状态。**
