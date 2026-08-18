# 基于体素的 PCG 世界生成与 Mesh 构建流程

当前实现可以概括为：

> **以 X/Z 体素柱为流送和 PCG 单元，以 32³ Section 为 Mesh 构建单元；工作线程生成纯数据，主线程提交体素世界与 Unity 对象。**

~~~mermaid
flowchart LR
    A["关卡/世界配置"] --> B["计算玩家附近所需体素柱"]
    B --> C["后台生成密度场与体素类型"]
    C --> D["矿物、Jigsaw、其他结构覆盖"]
    D --> E["主线程提交 InfiniteVoxelWorld"]
    E --> F["初始出生区/固定结构处理"]
    F --> G["截取 33³ Section 样本"]
    G --> H["后台 Marching Cubes"]
    H --> I["主线程创建 Mesh/Renderer"]
    I --> J["分帧生成 Collider/表面物件"]
    J --> K["世界 Ready"]
    L["采掘、放置、爆炸"] --> G
~~~

## 1. 配置入口

GameAssetCatalog 解析当前 LevelConfiguration，再取得 MinecraftWorldGenerationConfiguration。关卡的 WorldSeed 会覆盖世界配置中的种子。

目前真正的地形模式只有：

- InfiniteCaves：洞穴密度场。
- Superflat：规则平坦层。

DenseJigsawWorldConfiguration 不是第三种地形算法，而是叠加在洞穴世界上的规则层，控制有效高度、Jigsaw 密度、外置降落舱和传送门等。当前 Dense 世界有效高度为 **64 体素，即两个 32 高 Section**。

核心调度在 [MinecraftCaveInfiniteWorld.cs](../../Game/Runtime/MinecraftCaveInfiniteWorld.cs)。

## 2. 流式体素柱生成

世界按 X/Z 划分为 32 × 256 × 32 的体素柱容器，Y 不参与柱的流送寻址。容器尺寸定义在 [VoxelColumnChunkData.cs](../../Game/Runtime/Voxels/VoxelColumnChunkData.cs)。

运行时根据玩家位置：

1. 计算需要保留的 X/Z 柱集合。
2. 将缺失柱加入生成队列。
3. 取消已经离开需求范围的任务。
4. 回收超出保留范围的数据和 Mesh。

默认半径 4 的圆形范围包含 49 个柱；初始出生阶段会使用更小的加载区域。

## 3. 单柱 PCG 顺序

每个缺失柱在后台任务中生成，顺序为：

1. InfiniteCaves 使用 [MinecraftCaveDensityInterpolator.cs](../../Game/Runtime/MinecraftCaveDensityInterpolator.cs) 按绝对世界坐标采样洞穴密度场。
2. 根据 density ≥ isoLevel 判断实体；当前正式配置 isoLevel = 0。
3. 写入底部和有效世界顶部的 Bedrock。
4. [MinecraftOreFeatureGenerator.cs](../../Game/Runtime/MinecraftOreFeatureGenerator.cs) 替换部分实体体素类型，不改变地形密度形状。
5. [JigsawStructureGenerator.cs](../../Game/Runtime/Structures/JigsawStructureGenerator.cs) 写入 Jigsaw 结构。
6. [MinecraftStructureFeatureGenerator.cs](../../Game/Runtime/MinecraftStructureFeatureGenerator.cs) 写入其他结构 Feature。
7. 清空有效高度以上的数据，并重新封闭上下 Bedrock。

随机结果由世界种子和绝对坐标决定，因此不会因为柱的加载顺序不同而改变。

## 4. 数据提交与全局结构处理

生成完成后，主线程会：

1. 重新应用玩家采掘、放置等持久化体素覆盖。
2. 通过 AddChunkTakingOwnership 将密度和类型数组直接交给 [InfiniteVoxelWorld.cs](../../Game/Runtime/Voxels/InfiniteVoxelWorld.cs)，避免整柱复制。
3. 等待初始所需柱全部完成。
4. 执行出生点固定结构、降落区域净空和地面稳定处理。
5. Dense Jigsaw 使用外置降落舱时，会跳过普通的地形内出生结构挖掘。
6. 将所有需要的 Section 加入 Mesh 队列。

初始状态依次经历：

    Terrain → Structures → Meshes → Ready

## 5. Marching Cubes Mesh 构建

Mesh 不按整个 256 高体素柱构建，而是按 32 × 32 × 32 Section 构建：

1. 主线程截取当前 Section 以及正方向相邻柱的数据，形成 **33³ 样本快照**。
2. 后台线程调用 [MarchingCubesMesher.cs](../../Game/Runtime/Voxels/MarchingCubesMesher.cs)。
3. 根据密度在等值面两端插值顶点，而不是生成固定方块面。
4. 根据体素类型和体素组生成对应 SubMesh、材质边界及必要的组间接缝。
5. 输出纯数据形式的 VoxelMeshData。

Mesh 队列带有优先级和版本号：

- 玩家交互影响的 Section 使用高优先级队列。
- 构建期间再次变化时，旧版本结果会被丢弃并重新排队。
- 柱边界变化会同时标脏相邻柱或 Section，避免接缝残留。

## 6. 主线程挂载与后处理

后台构建结束后，主线程分预算执行：

1. 创建或更新 Unity Mesh。
2. 更新 MeshFilter、MeshRenderer、SubMesh 和材质。
3. 从对象池取得或复用 Chunk GameObject。
4. 分阶段更新 MeshCollider。
5. 上传洞穴表面数据并生成表面装饰、植被等对象。
6. 所需 Collider 和后处理完成后，将初始世界标记为 Ready。

因此，Unity 的 Mesh、GameObject、Renderer 和 Collider 操作全部留在主线程；后台线程只处理快照和普通数组。

## 7. 运行时修改闭环

采矿、放置和爆炸最终都会进入 SetGameplayVoxel：

    修改体素 → 保存 Gameplay Override → 收集受影响 Section → 高优先级重建 → 更新 Mesh/Collider

边界体素会连带刷新相邻 Section。被卸载的柱重新生成时，也会重新应用 Gameplay Override，因此玩家改造不会因为流送而丢失。体素完整性系统还可以把失去支撑的连通块转为动态刚体，但它位于体素修改和 Mesh 重建之后。

更完整的参数与依赖说明集中在 [MinecraftCaves世界生成与Voxel依赖.md](../../Game/Docs/MinecraftCaves世界生成与Voxel依赖.md)。
