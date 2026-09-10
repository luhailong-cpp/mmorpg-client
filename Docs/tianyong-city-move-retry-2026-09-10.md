# 天墉城交付重试验证（2026-09-10）

已按“try again”重新同步隔离副本、编译、运行完整自动走位，并重跑全部 Unity 测试。本报告及画面来自新轮次 **retry_20260910**。本次验收副本未发现新的失败项。

## 本次结果

| 检查 | 结果 |
| --- | --- |
| 受测版本 | 验收启动前，1,144 个文件与上次最终验证版本一致；本次受测副本再次核对仍一致 |
| 离线编译 | 141 个运行时 C# 文件，0 错误 |
| Unity 构建 | Succeeded，0 错误、503 条已有警告 |
| EditMode | 249/249，0 失败、0 跳过 |
| PlayMode | 13/13，0 失败、0 跳过 |
| 运行画面 | 134/134 张有效 PNG；8/8 缩放位置、6/6 障碍物后方站位 |
| 连续渲染 | 3,528 次显式 Camera.Render，帧 4–3531，逐帧 CSV 连续 |
| 相机边界 | 0 帧超过 0.001 世界单位容差；本轮最大几何溢出值 0.00006103516 世界单位 |
| 原交付包 | CRC、包内文件 SHA-256 和三份主要报告的相对链接复核通过 |

**并行修改说明：** 打包时，当前源仓库已有 16 个清单内文件与受测副本不同，集中于网络、Pet、启动代码和工程配置。本轮结果只对应已构建副本；后续并行变化不算已验证，也未被本任务覆盖。[逐文件差异和受测哈希](VerificationEvidence/tianyong-20260910/source-verification.json)记录了具体文件。

实际运行配置为 Unity 6000.6.0f1、Direct3D12、1920×1080。相机结果覆盖本次连续显式离屏渲染；不能据此宣称任意配置、任意站位的像素级绝对“零露底”。实际缩放的起点、终点、方向及跨度也已核验。

## 已补齐项目与新画面

八方向独立待机、名牌半透明底板、真实 HUD 三按钮画面和障碍物后方证据均保留并重新运行验证；南北跑步重复姿势和西侧灯柱遮挡修复仍通过测试。

- [本次 HUD 和名牌画面](VerificationEvidence/tianyong-20260910/00_00_runtime_hud.jpg)
- [八方向独立待机](VerificationEvidence/tianyong-20260910/idle-directions-review.jpg)
- [长中文名牌](VerificationEvidence/tianyong-20260910/02_00_nameplate_long_cjk.jpg)
- [灯柱同站位前景开关对照](VerificationEvidence/tianyong-20260910/foreground-before-after.jpg)
- [点击反馈](VerificationEvidence/tianyong-20260910/45_04_click_a.jpg)
- [西边缩放首次渲染](VerificationEvidence/tianyong-20260910/72_09_zoom_w_out_f1.jpg)
- [现有南北跑步资源八帧对照](VerificationEvidence/tianyong-20260910/run-ns-preview.jpg)

HUD 使用实际组件的离线绘制，未进行服务器请求测试。独立待机是每方向单张站姿；灯柱前景针对已复现的西侧灯柱交叠问题，其他任意站位不由这组截图自动证明。通行掩码保持现状。

## 复核记录

[验收摘要](VerificationEvidence/tianyong-20260910/coverage-summary.json) · [逐帧 CSV](VerificationEvidence/tianyong-20260910/camera-frames.csv) · [播放器日志](VerificationEvidence/tianyong-20260910/player.log) · [构建日志](VerificationEvidence/tianyong-20260910/build.log)

[EditMode XML](VerificationEvidence/tianyong-20260910/EditMode.xml) · [PlayMode XML](VerificationEvidence/tianyong-20260910/PlayMode.xml) · [测试摘要](VerificationEvidence/tianyong-20260910/test-summary.json) · [本次受测副本哈希](VerificationEvidence/tianyong-20260910/source-verification.json)

轻量预览包包含本报告、七张关键预览及上述验证记录；不包含大型 Player、全套无损截图和素材原始生成图。原始 134 张 PNG 保留在 `E:/work/tmp/citymove_shots/retry_20260910`。先前完整素材交付包 `E:/work/output/tianyong-city-delivery-20260909.zip` 经重新检查仍完整可用。

本次没有提交仓库，也没有覆盖并行 Pet / 界面刷新改动。
