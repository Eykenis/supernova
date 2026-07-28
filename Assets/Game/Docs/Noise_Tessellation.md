“noise on top of tessellation” 怎么实现
这句话可能指两种完全不同的方法。
方法 A：在提取网格前修改隐式场
这是体素/SDF 地形最自然的做法：
\[
F(p)=F_{\text{base}}(p)+A\,N(pf)
\]\(F_{\text{base}}\)：球、半球、平面等生成的基础场；
\(N\)：3D Perlin、Simplex、Worley 或 fBm；
\(f\)：频率；
\(A\)：振幅；
最后对 \(F(p)=0\) 做 Marching Cubes/tessellation。
伪代码：
float EvaluateGeneratedDensity(float3 p)
{
    float baseField = EvaluateRoomPrimitives(p);

    float noise =
        Simplex(p * frequency) * amplitude +
        Simplex(p * frequency * 2f) * amplitude * 0.5f +
        Simplex(p * frequency * 4f) * amplitude * 0.25f;

    return baseField + noise;
}
虽然官方说的是“把 noise 加到基础表面上”，但并不意味着它一定先生成三角形再位移；这种隐式场噪声在视觉结果上完全符合描述。
优点：
chunk 重建天然一致；
新挖出来的表面也可以得到一致噪声；
没有 UV；
3D 空间中不会出现明显方向性；
法线可以直接由场梯度得到。
缺点是噪声可能改变拓扑，生成孤立小石块和小洞，所以通常要限制它的作用带宽。
例如模板提供 inner/outer boundary，那么只允许最终表面在两层边界之间移动：
\[
\delta(p)=\operatorname{clamp}(A N(p),-\delta_{\text{inner}},\delta_{\text{outer}})
\]也可以给出口、任务区域等添加 mask：
float noiseMask =
    RoomBoundaryMask(p) *
    ExitProtectionMask(p) *
    ObjectiveProtectionMask(p);

return baseField + noise * noiseMask;
这很符合官方展示的绿色/黄色边界：一层决定基础轮廓，另一层限制噪声不能侵入过远。
方法 B：先 tessellate，再沿法线移动顶点
这才是字面意义上的 “noise on top of tessellation”。
foreach (Vertex v in mesh.Vertices)
{
    float n = FbmNoise(v.originalWorldPosition * frequency);
    v.position += v.baseNormal * n * amplitude;
}
流程是：
基础 CSG/SDF
  → 低模网格
  → 必要时细分
  → 在世界坐标采样 3D noise
  → 沿基础法线移动顶点
  → 重算法线和碰撞
必须注意：
噪声使用世界坐标，不能使用每个 chunk 的局部坐标；
chunk 边界的共享顶点必须得到完全相同的噪声；
应始终基于原始位置采样，不能每次重建都对已经位移的顶点再次加噪；
噪声最高频率的波长最好至少是网格边长的 2～4 倍；
位移过大会造成三角形翻转和自相交；
post-tessellation displacement 不改变拓扑，只改变表面轮廓。
这种方式特别适合 DRG 的低多边形视觉，因为可以在位移后保留 flat shading。
更实际的是混合方案
我更推荐：
低频、大尺度 noise
    → 加到 density/SDF
    → 改变真实轮廓和碰撞

中频 noise
    → 轻量顶点位移

高频细节
    → Shader normal / triplanar material
    → 不进入碰撞
例如：
float macro =
    Fbm(p * 0.035f) * 2.0f;

float ridges =
    RidgeNoise(p * 0.12f) * 0.45f;

float density =
    baseDensity + macro + ridges * biomeMask;
biome 之间只需要替换 noise stack：
Sandblasted：低频、平滑、振幅小；
Magma：ridged noise、domain warp、尖锐边缘；
Crystalline：Worley/ridge 加独立水晶 Actor；
Fungus：圆滑 fBm 加大量 debris；
Glacial：带方向性的层状/裂隙 noise。
动态挖掘时，基础生成场应保持不变，玩家修改作为独立 carve layer：
\[
F_{\text{final}}(p)=\operatorname{CSGSubtract}
\left(F_{\text{generated}}(p),F_{\text{playerCarves}}(p)\right)
\]或者生成时把带噪声的 density 烘焙到 chunk 数据中，之后只修改这些 density sample。这样重新 tessellate 时，旧表面不会“重新随机一次”。