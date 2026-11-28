# 青岚镇（`qinglan-town` / `original:legacy-lanxian-town`）双节地图客户端接入设计方案

> 日期：2026-11-28  
> 适用仓库：`mmorpg-client`（仅客户端离线走图与览景接入，未修改 C++/Go 或 Java 服务端）  
> 图仓来源：Azure `main` 提交 `d36d5575a8905f9a6d9381c0be2e875b2463edb0`（`designs/world-expansion-20261027/hd36-production-20261027/legacy-lanxian-town/continuous-20261127-v002/`，全部 64 个文件已核验）

---

## 1. 需求分析

1. **双节完整资源无损接入**：将图仓提交 `d36d5575` 交付的青岚镇春节（`spring`）与中秋（`mid-autumn`）两版 `4096×4096` 整图及各 `4×4 = 16` 张（共 32 张）`1024×1024` 运行切片，按原始 PNG 字节无损接入客户端 `Assets/Resources/World/QdaoMaps20261102/` Schema 2 正式管线，并逐文件冻结 SHA-256 与派生溯源。
2. **场景身份与联机安全边界**：按图仓交接说明与现存 `BaseScene` 占位槽位，将青岚镇登记为 `sceneId = 4`、`key = "qinglan-town"`、`name = "青岚镇"`、`revision = "qinglan-20261127-v002"`，并显式保持 **`clientOnly = true`**（`authoritativeServerRegistered = false`）：
   - 服务端 `BaseScene 4` 当前仍指向旧版 `lanxian_scene.bin`（`spawn = (180, 200)`，与新图 `[-154, 154]` 世界坐标不兼容），且按世界地图 V2 控图定调，场景 4 位于储备池、尚未开放正式联机传送。
   - 保持 `clientOnly = true` 既可立刻提供客户端离线双节走图（`Assets/Scenes/World/QinglanTown.unity`）与大世界地图「览景」页签双节切换预览，又严格阻断 `CurrentMapCatalog.FindForOnline(4u)` 与线上 `EnterScene(4u)` 错配，待后续服务端烘焙同源 `scene.bin` 并更新 `BaseScene.xlsx` 后，只需将 `catalog.json` 中 `clientOnly` 翻为 `false` 即可无缝转正。
3. **双节共用导航与有效相机边界**：两版外观在装饰落点外逐像素一致且三座桥保护区一致，按 `atlas.json` 坐标映射为两版绑定同一份 `walkmask.txt` 与严格裁切到可见画面的 `effectiveWorldRect`。
4. **双形态与规范约束**：不引入线程/阻塞调用/原生套接字/反射发射；复用现有 `CurrentMapCatalog` / `CurrentMapBuilder` / `CurrentMapTileStreaming` / `TianyongSandboxBootstrap` 管线，零额外第三方依赖，同时兼容微信/抖音小游戏（WebGL2 单线程）与 iOS/Android App（IL2CPP）。

---

## 2. 设计分析

### 2.1 几何与坐标合同（严格对齐 `atlas.json` 与 `MapDeliveryContract`）

- **画布网格**：`4×4` 方形分片，单块 `1024×1024`，完整画布 `4096×4096`，西北起行优先（`r01_c01` .. `r04_c04`）。
- **世界空间等比映射**：
  - `worldRect = { x: -154.0, y: -154.0, width: 308.0, height: 308.0 }`（`y` 为 Unity 世界 Z 南边；每块切片精确 `77×77` 世界单位，比例 `308 / 4096 = 77 / 1024 = 0.0751953125`，二进制精确无舍入误差）。
  - 像素 `(x, y)` → Unity 世界 `(X = -154 + 308·x/4096, 0, Z = 154 - 308·y/4096)`，与 `atlas.json` 完全一致。
- **可见画面与有效范围裁切**：
  - 可见内容矩形 `imageRect = { x: 0.0, y: 782.0, width: 4096.0, height: 2532.0 }`（上下各 `782px` 为非可玩透明 Alpha 衬边，画面内部 `y ∈ [782, 3314)` 100% 不透明 `alpha = 255`）。
  - `effectiveWorldRect = { x: -154.0, y: -95.197265625, width: 308.0, height: 190.39453125 }`（即 `X ∈ [-154, 154], Z ∈ [-95.197265625, +95.197265625]`），严格位于旧有效范围 `Z ∈ [-99, 99]` 与可见画面内，确保相机夹取永不露出上下衬边。
  - `referenceWidth = 1595, referenceHeight = 986`（`legacySourceLayoutPixels`），`|4096×986 - 2532×1595| = 116 ≤ 1595`，零误差通过 `MapDeliveryContract.ValidateGeometry`。
- **导航栅格**：
  - `cellSize = 1.0`，`walkmaskColumns = 308`，`walkmaskRows = 308`，总计 `94,864` 格 = `11,858` 字节（整除 8，零尾部填充位）。
  - 出生点位于西南牌坊内侧主路交叉口：像素 `(935, 2915)` → Unity 世界 `(-83.6923828125, 0, -65.1943359375)`。

### 2.2 双节共用导航设计与视觉复核

- 导航计划固化于 `Docs/VerificationEvidence/qinglan-20261127-v002/navigation-plan.json`：
  - `22` 组可行走道路/广场/桥面/石阶多边形与 `28` 组阻挡多边形，生成 `6,389` 可走格、`1` 个单连通分量。
  - `21` 处关键地标全连通：西南牌坊、西外环路、西岸集市、镇中石桥、东岸岔口、北侧正阶、北侧西/东农舍前场、北侧菜圃旁缓坡、西北木桥及瀑布北道/西道、东北大院石阶与前庭、东岸铺面广场、炉房前场、东侧外环山道与东出口、东南木桥、南岸水亭前路。
  - `26` 处阻挡探针覆盖上下透明衬边、瀑布潭与全段溪流、镇中石桥北护栏、牌坊双柱与屋檐、西/东市木桌、水亭、北侧两农舍与菜圃、东北主殿与浑天仪、两节装饰落点、炉房铁砧与火炉及南岸大树。
  - 已在春节与中秋两版原图上叠加渲染 `walkmask.txt` 逐格目视核验，确认三座桥仅开放桥面、石阶不越栏、双岸回环与北侧正阶/缓坡双线贯通。

### 2.3 透明衬边兼容处理与零冗余资产

- `d36d5575` 整图与上下两行切片（`r01_c01..c04`、`r04_c01..c04`）为 RGBA PNG，其中 `imageRect` 内 100% 不透明、`imageRect` 外为 `(0,0,0,0)` 透明衬边。
- 最小扩展 `tools/import_festival_maps.py` 支持批次级 opt-in 字段 `allowTransparentPadding`（默认 `false`，不改变任何既有批次严格不透明校验）：开启后仍严格要求 `imageRect` 内部像素 100% 不透明、`imageRect` 外部衬边 100% 透明，杜绝画面漏透明或衬边脏像素。
- Unity 导入侧既有 `CurrentMapTileImporter.cs` 与 `.meta` 已统一锁定 `alphaSource = None`、`textureFormat = RGB24`、`Uncompressed`、`mipmapEnabled = false`、`maxTextureSize = 1024/8192`，既保持磁盘 PNG 与提交 `d36d5575` 逐字节 SHA-256 一致，又满足运行时 `CurrentMapTileStreaming` 的 `RGB24` 校验与零无效透明通道显存开销。
- 溯源真实记录 `sourceNative = false, sourceDerived = true`（切片由 `4096×4096` 合成整图无损裁切，底层 6 张宿主内置原生回图与未披露模型/画质信息完整保留于 `source-evidence/`）。

### 2.4 客户端入口与测试同步

- 新增离线双节走图场景 `Assets/Scenes/World/QinglanTown.unity`（复用 `TianyongSandboxBootstrap`，锁定 `deliveredMapId = 4`、`deliveredAppearance = "spring"`、`lockDeliveredMap = 1`）。
- 在 `Assets/Editor/FestivalMapPreviewMenu.cs` 注册菜单 `MMORPG > Maps > 青岚镇双节走图`。
- 在 `Assets/Scripts/UI/Ugui/Gameplay/CityTravelUiRoot.cs` 为场景 4 补充青岚镇简介（在「览景」页签展示春节/中秋切换预览，保持 `OnlineAvailable = false`）。
- 更新 `Assets/Tests/EditMode/Tianyong/CurrentMapTests.cs`：新增 `InstalledQinglanTownSharesNavigationAcrossFestivalsAndStaysClientOnly` 回归用例，并同步原 `RuntimeSceneRoundTripRebindsNavigationAndCannotRestoreOldArt` 中对场景 4 的断言（仍验证 `EnterScene(4u)` 不加载且 `FindForOnline(4u) == null`，同时确认 `Find(4u)` 已登记为离线 `clientOnly`）。

---

## 3. 思想分析

- **单一管线与数据驱动**：不新增第二套地图加载器，直接复用经生产验证的 `QdaoMaps20261102` Schema 2 管线；未来服务端完成场景 4 导航烘焙后，客户端仅切换 `catalog.json` 的 `clientOnly` 标志即可平滑上线。
- **失败关闭与端到端哈希链**：从提交 `d36d5575` 原始整图/切片字节到 `selection.json`、`source-evidence/`、`source.json`、`tiles.json`、`walkmask.txt` 与 `catalog.json` 全链路 SHA-256 互锁，离线校验零外部图仓路径依赖。

---

## 4. 漏洞分析与防呆措施

1. **客户端误开线上场景 4 导致与旧服务端 `lanxian_scene.bin` 越界冲突**：通过 `clientOnly: true` 阻断 `FindForOnline(4u)` 与线上 `EnterScene(4u)`，并由 `CurrentMapTests` 与 `FestivalRegionMapTests` 双重断言回归锁定。
2. **RGBA 透明衬边被既有导入器拒收或被误导入为 RGBA32 触发 `CurrentMapTileStreaming` 格式拒绝**：在 `import_festival_maps.py` 增加 opt-in `allowTransparentPadding` 严格区段校验，配合 `CurrentMapTileImporter` 统一导入 `RGB24`（`alphaSource = None`）且相机严格夹取至不透明 `effectiveWorldRect`。
3. **跨平台换行符破坏 JSON/TXT 签名校验**：在 `.gitattributes` 追加 `/Docs/VerificationEvidence/qinglan-20261127-v002/**/*.json -text`（`QdaoMaps20261102` 下所有 `.json/.txt/.meta` 已受既有 `-text` 规则保护）。
4. **验证边界诚实声明**：当前环境未在带图形渲染帧的 Unity 播放器或真机上实测运行，本轮完成离线字节/SHA-256 校验、Roslyn 全量编译、Python 回归单测与生产 C# 契约/A* 寻路托管测试；Unity 实机画面、角色脚底尺度、纹理压缩及三端（Windows / 微信抖音小游戏 / App）实机表现仍待真机与 Unity 编辑器验收。

---

## 5. 步骤分析

1. 提取并校验提交 `d36d5575` 全部 64 个文件（含 Git LFS 实图），绘制并目视核验双节共用 `navigation-plan.json`。
2. 最小扩展 `tools/import_festival_maps.py` 与 `tools/tests/test_import_festival_maps.py`，支持 opt-in `allowTransparentPadding` 并确保既有测试全绿。
3. 生成冻结的 `Docs/VerificationEvidence/qinglan-20261127-v002/{batch.json, selection.json, navigation-plan.json}`，执行导入与离线独立校验。
4. 添加 `QinglanTown.unity(.meta)`、菜单项、地图窗描述、`.gitattributes` 与 `CurrentMapTests.cs` 回归用例。
5. 运行 Roslyn 编译门禁、Python 全量单测、C# 托管契约/导航/运行时切换测试并核对工作区 `git status`。

---

## 6. 执行步骤

1. 落盘本设计文档并验证 `tools/tests/test_import_festival_maps.py` 全部通过。
2. 冻结 `Docs/VerificationEvidence/qinglan-20261127-v002/` 批次配置、选择清单与共用导航计划，执行 `import_festival_maps.py import` 与 `verify`（同步复验历史批次零破坏）。
3. 补齐 `QinglanTown.unity`、`FestivalMapPreviewMenu.cs`、`CityTravelUiRoot.cs`、`.gitattributes` 与 `CurrentMapTests.cs`。
4. 依次执行 `tools/client_compile_check.ps1`、Python 单测与离线 verify、C# 托管契约/运行时/青岚镇寻路核验，确认零报错后汇报交付清单与剩余实机验证项。
