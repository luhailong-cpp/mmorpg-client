# 完整主城的生产发布与校验

## 当前部分分片交付（2026-10-25）

用户要求把所有已生成的完整分片先放入现有正式联机地图，不等待接缝修复。此次交付使用独立的 `CurrentMaps20261009/{main,village,island}/tiles.json` 合同，状态为 `partial_native_tiles`，保留当前布局底图填补缺块。冻结审计显式指定 `selectionPolicy: all_generated_current_layout`，允许接缝失败、邻片待修或 QA 缺失的完整候选；原始 PNG 字节、完整解码、4096 RGB（或整张 alpha 为 255 的 RGBA）、布局 SHA 和图格身份仍须通过校验。过时布局、损坏文件、格位错误和含占位布局的未完工预览不会因此变成可交付完整图。

每条审计必须明确 `internalQaPassed` 为 true、false 或 null，并保留实际 `formalAccepted`、`acceptanceStatus`、`externalStatus`。缺失 QA 时 `qaPath` 与 `qaSha256` 同为 null，不能声明内部通过或正式验收。无 `selectionPolicy` 的历史交付继续执行原严格内部 QA 规则；新策略仅改变本次部分交付的选择条件，不签发整图验收。

`tools/import_completed_map_tiles.py` 导入已审计的交付清单，`--verify` 在只有客户端仓库的环境中核对图片、尺寸、哈希、字节数、元数据和来源证据。主城为 16×16 网格，渔村、八仙岛为 14×14；坐标从左上角起，清单行列从 1 开始。资源校验 CI 在 Windows 和 Linux 检出后执行这些检查。

本批共 **519 张**原始 4096×4096 PNG：主城 **178 张**、渔村 **165 张**、八仙岛 **176 张**，合计 **13,108,398,493 字节**。资源导入及全量仓库校验已通过；内部 QA 为 131 张通过、50 张失败、338 张未确认，整图尚未验收，也未做实机画面验证。源图包含 518 张 RGB 和 1 张全不透明 RGBA，均保留原始 PNG 字节；具体证据见[本次资源记录](VerificationEvidence/generated-map-tiles-20261025/resources.json)。

空间查询接入 Amit Patel / Red Blob Games 的 [CC0 C# 六边形实现](https://www.redblobgames.com/grids/hexagons/codegen/output/lib.cs)，与 C++ 服务端使用相同的 20 世界单位 flat-top 布局、零原点及半值远离零舍入。通过 `WorldCoordinateConverter` 将 Unity Z／X 对应到服务器 X／Y。玩家本格加六邻格形成七格周边候选，同时纳入相机真实视野覆盖的格子；方形分片按矩形与六边形多边形相交关系建立索引，跨格分片不能仅登记到中心所在格。初始化只处理元数据，原 16×16／14×14 分片保持不变，不把整图均分成九块。

七格空间查询与纹理缓存上限是两个独立约束。当前加载器同时驻留和加载中的高清分片合计最多六张，最多两个并发请求；实际视野超过六张时其余位置显示当前底图。交付数量、来源和验证边界见[本次交付记录](VerificationEvidence/generated-map-tiles-20261025/README.md)。这条部分交付流程不修改下面旧 `CityTileManifest` 的完整数组校验，也不签发 `accepted_complete`。正式完整高清地图、外沿接缝和实机显存仍需各自验收。下面保留的是旧七外观完整交付工具的使用说明，不应把其中 2026-09 的库存或网格要求当成当前三张日景地图的状态。

## 历史完整交付流程

本流程按一套外观执行。当前美术只有局部候选，不能运行生产发布；现况见 [本次交付审计](VerificationEvidence/city-tiles4k-integration-20260921/delivery-audit.md)。`stage_city_tile_candidates.py` 仍只服务 Editor 候选检查。

## 交付输入

`tools/publish_city_tiles.py` 默认从脚本位置解析本客户端和相邻 `image/qdao_city_tiles_4k_20260916`，不依赖历史 `E:/work`。需要 Python 3.10+ 和 Pillow。本机可用的 Python 为：

```powershell
$CityTilePython = 'C:\Users\Administrator\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
Set-Location 'D:\luyuan\wuxingqitan\mmorpg-client'
& $CityTilePython tools/publish_city_tiles.py audit
& $CityTilePython tools/publish_city_tiles.py --output .utmp/penglai-day-contract.json contract --appearance penglai_day
```

`audit` 只报告库存，不签发通过。`contract` 生成包含 `delivery` 和验收记录示例的草案；草案有未通过项和占位SHA，必须拒绝发布。由真实美术交付填写并分别保存交付JSON、验收JSON及证据，所有美术文件路径相对美术批次根。不要将当前候选改名或将状态改为通过来满足工具。

交付JSON需明确 `purpose=production_city_tiles`、`status=accepted_complete`、美术外观名与版本；每套16×16、256项，按左上起点逐行排列。逐块记录文件、SHA、`role=production_tile`、全图像素坐标、世界坐标、原生来源记录与assembly引用/SHA。工具完整解码PNG并核对4096²、CRC、坐标、重复来源和SHA。

验收记录必须绑定同一外观、版本、256项清单摘要、验收人/时间及逐项证据SHA。先填好全部图块和坐标，再计算摘要：

```powershell
& $CityTilePython tools/publish_city_tiles.py digest --delivery '<完整交付JSON路径>'
```

美术验收涵盖整城布局、完整横缝/纵缝、四块交点、最近镜头清晰度和导航对齐。天墉还必须有剪影、遮挡、脚点、光照、最近镜头清晰度五项前景证据，并绑定实际被审核的客户端前景代码、旧纹理及导入meta版本。工具仅在这些记录齐备且SHA一致时生成 `legacyForegroundCompatible=true`，不提供跳过开关；这些记录是否真实通过仍需人工和游戏画面核对。

| 美术外观 | 正式资源目录（相对 `Assets/Resources/`） |
| --- | --- |
| tianyong_festival | World/CityTiles4K/tianyong/festival |
| penglai_day | World/CityTiles4K/penglai/day |
| penglai_mid_autumn | World/CityTiles4K/penglai/festival |
| donghai_day | World/CityTiles4K/donghai/day |
| donghai_lantern | World/CityTiles4K/donghai/festival |
| lanxian_day | World/CityTiles4K/lanxian/day |
| lanxian_spring | World/CityTiles4K/lanxian/festival |

正式 `manifest.json` 使用既有 `CityTileManifest`：schemaVersion=1、tilePixels=4096、columns=rows=16、worldRect=(50,0,300,300)。256个路径为 `World/CityTiles4K/{city}/{variant}/tiles/r01_c01` 等，不带扩展名；y表示世界Z。

## 暂存、发布与回退

以下命令中的尖括号内容需替换为真正交付/命令返回值。全局选项（`--project`、`--art-root`、`--output`、`--evidence-dir`）放在子命令前。

```powershell
& $CityTilePython tools/publish_city_tiles.py --output .utmp/delivery-validation.json validate --delivery '<完整交付JSON路径>'
& $CityTilePython tools/publish_city_tiles.py stage --delivery '<完整交付JSON路径>'
& $CityTilePython tools/publish_city_tiles.py publish --release '<stage返回的release>'
```

暂存位于 `.utmp/city-tiles4k/releases/`，不会进入Resources。发布前关闭本客户端Unity编辑器；发布工具检查项目占用并持有独占锁，不会代替用户关闭编辑器。完整新tiles在临时目录复制、逐张复验后才进入正式目录，manifest最后安装。旧目录整体保存在 `.utmp/city-tiles4k/transactions/`，不会从美术来源复制meta。

持久发布回执、来源映射、审核记录保存在 `Docs/VerificationEvidence/city-tiles4k-publications/{transaction}/`。`.utmp`内含完整暂存和回退字节，不属于Git提交；没有另行备份前，清理该目录会丢失回退能力。

```powershell
& $CityTilePython tools/publish_city_tiles.py rollback --transaction '<publish返回的transaction>'
& $CityTilePython tools/publish_city_tiles.py recover
```

`rollback` 恢复该次发布前的目录；首次发布则恢复为未发布。`recover` 处理进程中断留下的事务。恢复时核对保存的文件清单，遇到别的窗口改变过的内容会拒绝覆盖；被撤回的字节也保留在事务目录。普通发布异常会尝试自动恢复，原异常和未完成事务需一起检查。

## Unity真实导入校验

源PNG通过校验不代表Unity没有降采样。正式发布后，在本项目关闭时启动独立batch编辑器：

```powershell
$CityTileUnity = 'E:\Unity\6000.6.0f1\Editor\Unity.exe'
$CityTileProject = 'D:\luyuan\wuxingqitan\mmorpg-client'
$CityTileEvidence = Join-Path $CityTileProject ('Docs\VerificationEvidence\city-tiles4k-import-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $CityTileEvidence | Out-Null
$CityTileArgs = @('-batchmode', '-nographics', '-projectPath', ('"' + $CityTileProject + '"'),
  '-executeMethod', 'MmorpgClient.Editor.Tianyong.CityTileProductionVerification.VerifyBatch',
  '-cityTileEvidence', ('"' + $CityTileEvidence + '"'), '-logFile', ('"' + (Join-Path $CityTileEvidence 'unity.log') + '"'))
Start-Process -FilePath $CityTileUnity -ArgumentList $CityTileArgs -WindowStyle Hidden -Wait
```

换机器时指定本机Unity路径。校验器沿用 `CityTile4KImporter`，扫描七个正式入口，只对已存在manifest逐张导入核查并释放，不同时加载256纹理；记录实际尺寸、RGB24、SHA、mipmap及默认/平台导入设置。缺少某套时记录 `not_delivered`；全部缺少时退出码2，不能当成通过。某套失败退出码1；至少一套存在且全部已发布套数导入通过才退出码0。报告字段 `gameplayAcceptancePassed` 始终为false。

## 代码回归与游戏验收

```powershell
pwsh -File tools/client_compile_check.ps1
pwsh -File tools/run_city_tiles_tests.ps1
& $CityTilePython -m unittest discover -s tools/tests -p 'test_publish_city_tiles.py' -v
```

测试runner从本机csproj/安装目录解析Unity，在隔离工程复制并记录Manifest、Streaming、Importer、正式验证器和测试源码SHA；只替换固定世界矩形依赖。导入测试的合成4K图仅是测试夹具，不能作为美术成品或游戏截图。

真实验收必须从正式登录/进主城入口进入，确认 `AppBootstrap → TianyongMapRuntime → ConfigureCityTiles → CityTileStreaming` 加载本次manifest和4K纹理，再留存以下证据；沙盒只能补充：

- 最近镜头、连续跨块移动、四块交点、缩放、超宽屏及地图四边；每张截图记录外观、坐标和manifest SHA。
- 原导航、出生点、独立前景遮挡与人物脚点；天墉逐剪影检查旧前景近景清晰度。
- 已完成外观之间切换，目标可见块齐备后整体提交；缺manifest、缺图和加载失败时保留当前画面。故障注入使用隔离副本并记录被移除/替换的文件。
- 可见块加一圈、并发请求≤2、离开范围2秒释放；记录 `ResidentTileCount` / `PendingTileCount` 时间序列及Unity Profiler的CPU/GPU帧时间、峰值内存和显存。包含换肤时旧层与候选层重叠的峰值，不把RGB24字节估算当实测。

未完成这些项前，发布/导入/逻辑测试结果均不能标成“新完整地图实机验收通过”。
