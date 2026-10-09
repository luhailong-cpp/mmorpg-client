# 主城 NPC 接入 · 2026-10-09

23 位已交付的主城静态 NPC 已接入客户端源码，使用现行 6×6 主城的坐标。人物、姓名和阴影属于独立图层，主城底图、切片、前景和导航文件没有改动。主城图正在并行重绘，后续可以直接调整人物坐标，不需要重画人物或合并到底图。

## 入口

- [位置与显示配置](../Assets/Resources/World/Tianyong/Npcs/placements.json)：23 个独立脚底位置，Unity X/Z 世界坐标。
- [运行时图层](../Assets/Scripts/World/Tianyong/TianyongCityNpcs.cs)：在 `TianyongMapBuilder.Build` 成功应用主城画作之后创建；随地图卸载释放。
- [素材来源](CityNpcs.sources.json)：由 `image/designs/city-npcs-20260924/transparent-1024` 原样复制，23 张文件的 SHA-256 与已交付素材一致。高清重绘目录尚未完成，未混用未验收样稿。
- [全城排布预览](VerificationEvidence/city-npcs-20261009/npc-layout-overview.jpg) · [西市](VerificationEvidence/city-npcs-20261009/west-market.jpg) · [北区](VerificationEvidence/city-npcs-20261009/north-districts.jpg) · [南区](VerificationEvidence/city-npcs-20261009/south-gardens.jpg)

以上图片为实际底图切片与 NPC 素材按配置合成的**排布预览**，不是 Unity 实机截图。姓名排版仅用于检查位置，游戏里由现有 WorldNameplate 绘制，人物使用玩家相同的脚底深度排序。

## 已完成检查与限制

- 23 张独立 1024×1024 RGBA，真实透明背景；无图像再生成、裁切或重采样，运行时由配置控制显示尺寸。
- [排布检查](VerificationEvidence/city-npcs-20261009/layout-validation.json)：23/23 位于现行导航可达区域，从当前出生点可达；人物可见包围框没有相交。解析生产地图的导航蒙版，按八方向并禁止斜穿墙角进行静态连通性核验。
- 查看分区预览后移开了西市的重叠人物，调整杨镖头与云游大仙使其避开装饰。大路及广场中心保持通行。
- [离线编译](VerificationEvidence/city-npcs-20261009/offline-compilation.json)：372 个运行时源文件、2 个编辑器文件、2 个相关测试文件均 0 错误。测试源文件通过编译不代表测试已执行。
- [Unity 执行受阻](VerificationEvidence/city-npcs-20261009/unity-validation-blocked.json)：Unity 6000.6.0f1 批处理在进入工程前因 `No valid Unity Editor license found` 返回 198。真实导入、引擎截图、EditMode 测试及新版播放器构建尚未完成。未改动许可证或已运行的客户端。

本次范围为静态视觉摆放：沿用姓名、朝向镜头、接触阴影和遮挡排序；没有新增碰撞、服务端 NPC 实体、对话、商店或任务交互。运行时只在绘制主城挂载；程序化地图和其他地区不复用这些坐标。

## 后续换图与复验

人物可见高度为世界单位，`pivotX/pivotY` 是图片左下角为原点的脚底锚点，`visibleHeightFraction` 来自素材透明包围框。坐标的旧图换算为 `x = 50 + pixelX / 20.48`、`z = 300 - pixelY / 20.48`，旧图为 6144×6144；新图仍使用 300×300 世界范围时，直接保持世界坐标，按新道路逐项复查。

修改 `placements.json` 即可移位。不要运行临时目录里的初版位置草稿覆盖本文件；这里已包含人工视觉修订。

复现素材同步和排布预览（Python 需 Pillow）：

```powershell
python tools/sync_city_npcs.py
python tools/preview_city_npcs.py
pwsh -File tools/client_compile_check.ps1
```

Unity 许可证恢复且主项目编辑器关闭后，执行 `MmorpgClient.Editor.Tianyong.TianyongNpcVerification.VerifyBatch`（`-batchmode`，不要加 `-nographics`），输出真实引擎截图和验证报告。EditMode 可筛选 `TianyongCityNpcTests` 与 `TianyongPaintedCityTests`。这些入口已经保存并通过离线编译，仍需实际运行。
