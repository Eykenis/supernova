# Point / Spot 柔化衰减

项目使用 URP Forward。URP 的 Point Light 和 Spot Light 默认以近似反平方曲线衰减，
提高 `Intensity` 会同时放大近处和远处，因此无法只靠强度解决“远处太暗、贴脸太亮”。
增大 `Range` 可以把末端裁切推远，但不会改变主体衰减曲线。

`Supernova/Lighting/Soft Falloff Lit` 保留 URP Lit 的 PBR、法线、金属度、阴影、
光照贴图、雾和后处理兼容性，只修改逐像素附加光中的 Point / Spot 衰减。
方向光保持 URP 标准行为。

## InfiniteCaves 默认值

`MinecraftCaveInfiniteWorld` 的 `Punctual Lighting` 参数控制所有使用该 Shader 的洞穴材质：

- `Punctual Light Falloff Power = 0.55`：`1` 等于标准曲线；数值越低，
  中远距离越亮，同时近距离峰值越平缓。
- `Punctual Light Attenuation Limit = 1.5`：限制近场衰减项，防止贴脸过曝。
- `Punctual Light Multiplier = 1`：最终统一倍率，通常保持为 `1`。

石壁运行时材质、`Ore.mat` 和 `Bedrock.mat` 已接入这个 Shader。

## 推荐调参顺序

1. 先把灯的 `Range` 调到实际需要照亮的距离，Spot Light 同时确认锥角。
2. 保持 `Intensity` 只负责整体亮度。
3. 远处仍暗时逐步把 `Falloff Power` 从 `1` 降到 `0.7`、`0.55`。
4. 贴脸仍亮时降低 `Attenuation Limit`，不要继续压低灯光强度。
5. 如果只有高光过曝，优先降低材质 `Smoothness` 或 Bloom，而不是改变衰减。

当前 Shader 的自定义衰减位于 Forward Pass。若项目以后切换到 Deferred Renderer，
需要为 Deferred Lighting 增加对应实现。

## 共享衰减曲线

`SoftenedPunctualAttenuation()` 已抽出到
`Assets/Game/Materials/Lighting/SoftFalloffAttenuation.hlsl`，
由本 Shader 与洞穴草地 Shader 共同 `#include`，两者因此处于同一条衰减曲线上。
改动该函数会同时影响石壁与植被。

草地 Shader 不走标准 URP 光照循环——实例化绘制在 Forward 下拿不到逐对象光照索引，
需自行遍历全局灯光数组。原因与做法见 [洞穴草地渲染.md](洞穴草地渲染.md)。
