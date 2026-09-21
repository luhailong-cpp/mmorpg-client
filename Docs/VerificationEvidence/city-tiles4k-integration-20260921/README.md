# 主城4K客户端接入准备与验收记录（2026-09-21）

**本次正式接入0套。最新美术交付仍为24个局部候选，完整主城0/7套，正式验收0/1792块。** 因此按本次任务的无完整包分支交付可复用发布、回退和验证工具；未创建任何正式Resources manifest，未把候选/布局参考/旧Editor截图作为完整地图发布。

## 美术交付

实际制作根为 `D:\luyuan\wuxingqitan\image\qdao_city_tiles_4k_20260916`。采用2026-09-21最新status、production_catalog和current-batch，9月19日交接的19块计数已过时。

| 外观 | 已选局部候选 | 尚无选定候选坐标 | 完整交付 / 正式接入 |
| --- | ---: | ---: | --- |
| 天墉节庆 → tianyong/festival | 6 | 250 | 未完成 / 未接入 |
| 蓬莱日景 → penglai/day | 3 | 253 | 未完成 / 未接入 |
| 蓬莱中秋 → penglai/festival | 3 | 253 | 未完成 / 未接入 |
| 东海日景 → donghai/day | 3 | 253 | 未完成 / 未接入 |
| 东海元宵 → donghai/festival | 3 | 253 | 未完成 / 未接入 |
| 揽仙日景 → lanxian/day | 3 | 253 | 未完成 / 未接入 |
| 揽仙春景 → lanxian/festival | 3 | 253 | 未完成 / 未接入 |

24张候选全部完成PNG CRC与完整像素解码、4096尺寸、坐标、图像/QA/assembly SHA核验。502条保留原生来源核对文件/来源记录SHA及尺寸；原生来源没有宣称全部再次完整解码。读取前后美术账本SHA稳定。1768个坐标尚无选定候选，并不表示这些坐标都未开工；现有24候选也尚未转为正式验收块。

完整坐标缺口、版本路径、来源SHA和审核范围见 [delivery-audit.md](delivery-audit.md) 与 [delivery-audit.json](delivery-audit.json)。

## 本次新增工具

- `tools/publish_city_tiles.py`：正式交付合同、库存审计、来源/全像素校验、Resources外暂存、完整目录发布、manifest最后安装、来源映射和证据快照、前版回退、中断恢复。天墉兼容标志要求逐项前景验收，且绑定被审核的客户端前景文件SHA。
- `Assets/Editor/Tianyong/CityTileProductionVerification.cs`：检查正式清单的16×16路径和世界矩形，逐张加载实际导入纹理，检查4096/RGB24/sRGB/Clamp/Bilinear/不可读/无mipmap及四平台覆盖，记录SHA后释放。没有正式manifest时不能输出通过。
- `tools/run_city_tiles_tests.ps1` 与 `tools/tests/`：本机路径解析、隔离Unity测试、精确源码SHA、真实4K导入恢复测试及发布事务故障测试。

具体交付合同与命令见 [生产发布与校验](../../CityTilePublishing.md)。暂存与完整回退字节位于已忽略的 `.utmp/city-tiles4k`；小型发布证据另保留在Docs。本轮未对真实美术执行stage/publish。

## 现有游戏链路核对

正式入口为 `AppBootstrap.Awake → WorldMap.Initialize → GameClient.OnSceneEntered → TianyongMapRuntime.EnterScene`。天墉经 `TianyongMapBuilder / TianyongPaintedCity` 配置festival，另外三城经 `FestivalRegionMap` 配置day/festival。`TianyongMapRuntime.LateUpdate` 先更新相机再执行 `UpdateCityTiles`。这些是源码核对，不是本轮新地图实机证明，来源文件SHA见 [runtime-chain-audit.json](runtime-chain-audit.json)。

未修改运行时加载器、导航、出生点或世界坐标。现有行为保持：当前可见范围加一圈预载、最多2个并发请求、离开范围2秒释放；换肤等目标当前可见块齐备再提交，失败保留当前画面。天墉仍保留未审前景的阻止启用门槛。

## 本次验证

最终验证如下；成功记录必须连同源码SHA读取。历史9月17日21项结果与9月18日19候选Editor结果不计入本轮通过数。

| 验证层 | 本次实际结果 | 证据 |
| --- | --- | --- |
| 当前真实客户端Roslyn编译 | 最终356个运行时文件，0错误，exit=0；编译前后源码SHA稳定 | [最终编译结果](validation/runtime-final-compile/summary.json)、[源码SHA](validation/runtime-final-compile/source-hashes.json) |
| Unity 6000.6.0f1隔离EditMode | 29/29通过，0失败/0跳过；原有21项+新增8项，副本SHA与当前受测源一致且运行期间不变 | [summary.json](validation/summary.json)、[测试XML](validation/editmode-results.xml)、[受测源SHA](validation/source-hashes.json) |
| 真实4K导入行为 | 合成4096²纹理实际RGB24/sRGB/Clamp/Bilinear/不可读/mipmapCount=1；四平台先设置2048覆盖，再重导入，均清除并恢复4096 | [actual-import-settings.json](validation/actual-import-settings.json) |
| 无正式发布包门槛 | 隔离工程正式验证器exit=2，符合预期；未宣称导入或游戏验收通过 | [production-gate-summary.json](validation/production-gate-summary.json) |
| 生产发布工具回归 | 27/27通过，0失败；测试前后工具/测试源码SHA一致 | [publisher-test-summary.json](publisher-test-summary.json)、[完整输出](publisher-tests.txt) |

隔离依赖、Unity参数和夹具差异见 [invocation.json](validation/invocation.json)。测试日志存 `validation/unity-editmode.log`；首轮许可证沙箱失败与缺失physics测试夹具依赖的记录保留在attempt目录，修正执行环境/夹具依赖后的最终运行才计为通过。主项目编辑器未被打开或关闭。

其他窗口在07:52编译后继续修改两个角色/战斗文件；本窗口保留这些变化，并在07:58 EDT重新编译最新356文件，仍为0错误，编译期间SHA稳定。早一轮结果保留，不当作最终源码快照。

发布工具测试涵盖缺块/错序/错误坐标/SHA/损坏PNG/重复来源/未审前景、暂存污染、manifest末写失败、首次发布失败、回退、异常中断恢复和并行改动保护。PNG测试真实解码4096图；事务测试使用小图及解码mock，不能称作真实256块发布。独立故障复验见 [publisher-review.md](publisher-review.md)。

Windows原生独占锁已在临时项目用真实系统句柄验证：持有时其他句柄被拒、释放自动删锁、可再次获取；该测试仅将进程枚举替换为空，见 [publisher-native-lock.json](publisher-native-lock.json)。本机真实进程枚举下存在不能明确绑定projectPath的Unity实例，完整发布保护会拒绝执行；未关闭任何用户编辑器、未绕过保护。没有真实完整交付，因此本轮也未尝试正式发布。

已执行当前美术catalog的生产输入负向校验，退出码2，错误明确为候选/布局参考/预览不属于生产交付；正式资源目录仍不存在。证据：[publisher-command-results.json](publisher-command-results.json)、[candidate-production-rejection.txt](candidate-production-rejection.txt)、[publisher-audit.json](publisher-audit.json)。

## 尚未达到的验收层

由于没有一套完整正式图，以下均为**未执行/未通过新图验收**，没有对应的新完整地图游戏截图或性能实测：

- 实际游戏入口加载本次完整manifest与256块范围的真实4K资源。
- 最近镜头、连续跨块、四块交点、缩放、超宽屏及地图边缘的新图画面。
- 新图导航/出生点对齐，独立前景遮挡、人物脚点、光照和近景前景清晰度。
- 真实完整外观间切换及实际图块故障注入。
- 实际设备纹理驻留时间序列、峰值内存/显存、CPU/GPU帧时间。

逻辑测试使用隔离固定世界矩形夹具；真实4K导入测试使用合成测试PNG。它们不会替代新美术验收。每完成一套真实交付即可单套校验发布，再从正式入口完成上述验收，无需等待七套齐备。

美术仓保持只读；其他窗口对GameClient、角色、美术目录等已有或并行改动均保留。本窗口未提交、未推送。
