# 主城少年八方向独立待机素材 QA（2026-09-09）

本次新增的是八方向、每方向一张独立中性站姿；不是多帧呼吸循环。独立待机接入后另行修复了原有 N/S 跑步条的重复帧，见[南北跑步 QA](tianyong-run-ns-2026-09.md)。新素材由内置 image_gen 依据既有角色的八方向图编辑生成，经 generate2dsprite 处理后接入 `QdaoBoySpriteAnimator`。

## 运行时行为

- 新建角色显示 `idle_S.png`；移动继续使用按距离驱动的跑步条。
- 停止后沿当前跑步周期推进到既定落脚阶段，然后显示对应的 `idle_N/NE/E/SE/S/SW/W/NW.png`，保留最后朝向。
- 运行时资源位于 `Assets/Resources/World/Characters/QdaoHeadbandBoy/`，八张均为 512×512 RGBA PNG，PPU=52，pivot=(0.5,0.08)。缺失或尺寸不兼容会明确报警，不默认为旧跑步帧。
- 独立待机姿态可见双脚落地、身体直立、双手下垂。头带、青绿马甲、米白衣裤及腰侧葫芦按原角色参考保留；对照图可直接检查画风和姿态差异。

## 像素和视觉校验

严格素材 QC：8/8 帧有效，空帧/源图越格/输出碰边/贴图裁切均为 0；`body_scale_cv=0.04742`（阈值 0.08），`anchor_y_std=0.00527`（阈值 0.05）。处理时全部帧使用同一个缩放系数 1.1332266667，不做逐方向独立缩放。

导出后全部帧再统一向下平移 2px，与既有脚点校准：alpha>8 的最低像素距图片下缘均为 41px。新待机身高 398–424px；与各方向旧收势帧的高度比为 0.9528–1.0424，即最大约 4.7% 差异，未使用运行时按动作缩放。最终透明图已在深灰底视觉检查，人物四周无可见洋红底、断肢或裁边。

| 方向 | 新待机身高 px | 原收势帧身高 px | 比值 |
| --- | ---: | ---: | ---: |
| N | 398 | 410 | 0.9707 |
| NE | 418 | 436 | 0.9587 |
| E | 411 | 422 | 0.9739 |
| SE | 413 | 425 | 0.9718 |
| S | 409 | 420 | 0.9738 |
| SW | 424 | 445 | 0.9528 |
| W | 421 | 417 | 1.0096 |
| NW | 418 | 401 | 1.0424 |

[原跑步停帧 / 新独立待机对照](ArtEvidence/qdao-idle-20260909/before-after-game-scale.jpg)（每个方向左旧右新；按相同缩放显示）

[最终八方向预览](ArtEvidence/qdao-idle-20260909/idle-directions-preview.jpg)

## 回归检查范围

已更新 `Stopping_FinishesTheCycleIntoDedicatedIdle_AndStartingLeavesIt`，保留停止时逐帧收势和启动步态检查；新增 `EveryDirection_StopsOnItsOwnDedicatedIdleTexture`，遍历八方向并检查最终状态、朝向、实际纹理引用、PPU 与脚点。运行时 Unity 执行结果由统一验收报告记录；本素材 QA 不把静态检查当作 Unity 运行通过。

## 核查中发现的原跑步资源缺口（现已补齐）

`QdaoRunAssetTests.DirectionalRunStrips_HaveDistinctUpperBodyPosesAndGroundedFeet` 的失败源于已有跑步条，而不是新增 idle。逐像素复核采用与测试一致的上方 384px RGBA、Unity 自下而上的像素行顺序和 SHA-256：

| 跑步方向 | 8 帧中唯一上身图数 | 重复组（从 0 开始） |
| --- | ---: | --- |
| N | 5 | 0=4，1=3，5=7 |
| S | 5 | 0=4，1=3，5=7 |
| NE / E / SE / SW / W / NW | 每组 8 | 无 |

原测试先遇到 N 就退出，因此单次失败信息没有继续列出 S。原跑步各帧脚点仍为距下缘 40–41px，alpha>8 覆盖率约 0.17–0.23，脚点与背景覆盖检查本身没有失败。上述是替换前的测量。现已重新生成并接入 N/S 各八帧跑步条，两组上身姿势均为 8/8 唯一，脚点统一为距下缘 41px；保留原测试阈值。前后资源、哈希与处理记录见[南北跑步 QA](tianyong-run-ns-2026-09.md)。

## 可复查素材与参数

- [生成参考](ArtEvidence/qdao-idle-20260909/run-reference-preview.jpg)、[原始生成图](ArtEvidence/qdao-idle-20260909/raw-sheet.png)、[完整提示词](ArtEvidence/qdao-idle-20260909/prompt-used.txt)。工具实际输出尺寸为 1774×887，处理器按 2×4 等格裁切；最外余量像素不包含人物。
- [透明清理图](ArtEvidence/qdao-idle-20260909/raw-sheet-clean.png)、[处理器透明图集](ArtEvidence/qdao-idle-20260909/sheet-transparent.png)、[方向轮换 GIF](ArtEvidence/qdao-idle-20260909/animation.gif)（GIF 为方向预览，不是待机呼吸动画）。
- [严格处理元数据](ArtEvidence/qdao-idle-20260909/pipeline-meta.json)、[最终运行时 PNG 像素测量及 SHA-256](ArtEvidence/qdao-idle-20260909/runtime-qc.json)、[已有跑步各方向完整上身哈希组](ArtEvidence/qdao-idle-20260909/existing-run-upperbody-hashes.json)。

处理器参数：`process --target player --mode idle --rows 2 --cols 4 --cell-size 512 --fit-scale 0.83 --align feet --shared-scale --scale-strategy fit --component-mode largest --strict-qc --max-body-scale-cv 0.08 --max-anchor-y-std 0.05`。最后统一平移 +2px 后导出 `idle_*`；图集/GIF 为该最终平移前的处理产物，运行时 PNG 的最终测量以 runtime-qc.json 为准。
