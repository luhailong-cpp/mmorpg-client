# 南北跑步重复帧修复与 QA（2026-09-09）

本轮完整 EditMode 验收暴露旧 walk_N / walk_S 的重复上身姿势：各八帧只有五种，上身重复组为 0=4、1=3、5=7。原测试遇到 N 即失败，单次报错未继续列出 S。本次用内置 image_gen 参照既有少年角色生成并处理替换条，保留原纹理 .meta / GUID、4096×512 条尺寸、PPU=52 和脚点约定，未放宽测试。

## 素材与运行行为

每方向八帧，含左右腿交替及手臂摆动。运行时仍按移动距离推进循环；停止后沿当前周期收势并切入八方向独立待机。其他六个方向跑步条不变。本次不是补充待机呼吸动画。

[最终南北八帧对照](ArtEvidence/qdao-run-ns-20260909/run-ns-preview.jpg) · [约 13 FPS 循环预览](ArtEvidence/qdao-run-ns-20260909/run-ns-loop.gif)

## 处理与测量

原始生成图按 2×4 格切分；N 先建立共享缩放配置，S 使用同一配置，不按单帧独立缩放。采用 generate2dsprite 的 largest 连通域透明清理、脚点对齐、严格 QC，随后所有帧统一向下平移 2px，再横向拼成运行条。

| 指标 | N | S |
| --- | ---: | ---: |
| 有效帧 / 总帧 | 8 / 8 | 8 / 8 |
| 空帧 / 碰边 / 裁切 | 0 / 0 / 0 | 0 / 0 / 0 |
| 处理器身体尺寸变异系数（阈值 0.08） | 0.01265 | 0.00980 |
| 脚点 Y 标准差（阈值 0.05） | 0.00389 | 0.00139 |
| 最终人物高度 px | 419–424 | 423–424 |
| 最终距下缘脚点 px（alpha>8） | 每帧 41 | 每帧 41 |
| 最终上身唯一图数 | 8 | 8 |

上身哈希取图像上方 384px RGBA，按 Unity 自下而上行序计算 SHA-256，与原测试口径一致。已在深灰底检查角色轮廓、姿态、透明边缘和统一脚点；哈希证明像素不同，视觉对照用于复核姿势。最终 Unity 测试与运行截图以[总验收报告](tianyong-city-move-2026-09.md)为准。

## 可复查文件

- 原始生成：[N](ArtEvidence/qdao-run-ns-20260909/raw-N.png)、[S](ArtEvidence/qdao-run-ns-20260909/raw-S.png)。参照旧角色，要求八阶段前进跑步、双脚交替、身份服装一致、洋红底等格图集；这是生成意图摘要，不冒充逐字工具提示词。
- 替换前条：[N](ArtEvidence/qdao-run-ns-20260909/before-walk_N.png)、[S](ArtEvidence/qdao-run-ns-20260909/before-walk_S.png)。最终条：[N](ArtEvidence/qdao-run-ns-20260909/walk_N.png)、[S](ArtEvidence/qdao-run-ns-20260909/walk_S.png)。
- [N 处理元数据](ArtEvidence/qdao-run-ns-20260909/pipeline-meta-N.json)、[S 处理元数据](ArtEvidence/qdao-run-ns-20260909/pipeline-meta-S.json)、[共享缩放配置](ArtEvidence/qdao-run-ns-20260909/run-scale-profile.json)、[最终像素指标与哈希](ArtEvidence/qdao-run-ns-20260909/runtime-qc.json)。

参数：`process --target player --mode run --rows 2 --cols 4 --cell-size 512 --fit-scale 0.83 --align feet --shared-scale --scale-strategy fit --component-mode largest --strict-qc --max-body-scale-cv 0.08 --max-anchor-y-std 0.05 --duration 77`。N 用 `--write-scale-profile`，S 用 `--scale-profile`。循环 GIF 是预览，游戏速度仍由距离驱动逻辑决定。
