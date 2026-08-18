# Supernova 体素地形技术调研

本目录整理了项目当前体素地形相关实现，内容来自对实际代码的梳理，而不是通用算法介绍。

## 文档索引

1. [体素地形破坏与矿物刚体生成](./01_体素地形破坏与矿物刚体生成.md)
2. [射线如何确定第一个相交体素](./02_射线如何确定第一个相交体素.md)
3. [范围挖掘冲击力的传播与衰减](./03_范围挖掘冲击力的传播与衰减.md)
4. [柏林噪声洞穴地形生成算法](./04_柏林噪声洞穴地形生成算法.md)
5. [脱落体素块的坐标转换与交互](./05_脱落体素块的坐标转换与交互.md)

## 地形噪声示意图

| 类型 | 作用 | 图片 |
|---|---|---|
| Cheese | 大型洞室 | [Cheese Noise](./Images/Cheese_Noise_Schematic.svg) |
| Spaghetti | 粗主通道 | [Spaghetti Noise](./Images/Spaghetti_Noise_Schematic.svg) |
| Noodle | 细支路和捷径 | [Noodle Noise](./Images/Noodle_Noise_Schematic.svg) |
| Pillar | 洞内石柱 | [Pillar Noise](./Images/Pillar_Noise_Schematic.svg) |

> 图片是用于解释算法形状的示意图，不是游戏实际运行截图。

