# 原创地图「桂灯集」双节交付候选客户端预览接入设计（2026-11-24）

> 状态：**交付候选本地预览接入（`DELIVERY_CANDIDATE_NOT_FORMAL`）**，不等于正式验收通过，不写入正式联机地图目录（`QdaoMaps20261102` / `CurrentMapCatalog`），不推断或改动世界坐标、碰撞与导航。

## 1. 需求分析

- **接入素材**：`D:/work/image/.codex-artifacts/original-all-resume-20261123/guideng/delivery-candidate-v001`
  - 共同底图：`guideng-shared-neutral-4096x2731.png`（4096×2731，RGBA 但全不透明 `alpha=(255,255)`）
  - 春节整图与切片：`guideng-spring-4096x2731.png` + `spring/`（12 张无损 PNG + `manifest.json`）
  - 中秋整图与切片：`guideng-midautumn-4096x2731.png` + `midautumn/`（12 张无损 PNG + `manifest.json`）
  - 网格规格：4096×2731 矩形画布、4 列×3 行共 12 片；前两行每片 1024×1024，第三行每片 1024×683，不缩放、不补边，左上角原点 `(0,0)` 半开矩形坐标。
- **核心目标**：
  1. 资源完整导入客户端 `Assets/Resources/`，运行时零外部路径依赖，保留来源、生成记录摘要与 SHA-256 校验信息。
  2. 严格按清单左上角坐标与真实尺寸加载切片，精确处理末行 683px 高度，避免拉伸、错位、NPOT 重缩放或补边接缝。
  3. 复用现有分片预览查看器与外观切换机制，支持春节/中秋原地切换，不影响既有 36 张端游静态图及 12 张 `QdaoMaps20261102` 正式/离线图。
  4. 核实桂灯集在客户端与服务端的既有世界范围、镜头、出生点、通路与导航配置；资料不足时仅提供可切换预览并列出待确认项。
  5. 在 Unity 客户端实测两节整体比例、正常（1:1 原像素）与最近镜头清晰度、切片接缝（含末行 1024↔683 横缝）、道路与入口、外观切换，以及真实导入尺寸、格式与压缩设置。

## 2. 桂灯集既有配置核实与待确认项

经核查以下权威仓库与清单：
- 客户端 `Assets/Resources/World/QdaoMaps20261102/catalog.json`、`Assets/Resources/World/CurrentMaps20261009/`、`Assets/Resources/World/PcOriginalPreview20261123/catalog.json`：无 `original:guideng-market` / 桂灯集登记（历史 `map28007` 仅因早期暂存目录命名含 `guideng-market/28007`，实际属于端游 `28007`，非原创桂灯集）。
- 同级服务端 `D:/work/xuanming-server-mmo-cpp`：`generated/tables/basescene.json`（ID 1–21）、`generated/tables/world.json`（ID 1–16）、`data/scene_nav_bin/`（`current_maps_20261009.json`、`festival_maps_20261109.json`、`authored_maps_20261112.json`、`pc_original_static_backgrounds_20261123.json`）：无桂灯集 `sceneId`、出生点、世界矩形或导航数据。
- 美术侧契约（`CONTINUOUS_CANVAS_CONTRACT_v001.json`、`CONTINUOUS_PARTITION_PLAN_v001.md`、`guideng-walkable-layout-proposal.json`、`delivery-candidate-v001/README.md`）：明确标记 `DELIVERY_CANDIDATE_NOT_FORMAL`、`worldCoordinatesAndNavigationChanged: false`、`clientCollisionAccepted: false`、`clientNavigationAccepted: false`、`actualWalkingAreaMeasured: false`，且 4096×2731 尺寸系参考端游 01000 商铺尺度的目视估算。

**待确认项（正式入图与走图前必须补齐，本次不擅自推算）**：
1. 权威服务端 `BaseScene` 场景编号（`sceneId`）与客户端注册键（`key`）。
2. 游戏世界范围（`worldRect` / `effectiveWorldRect`）、像素-世界单位比例（与角色高度、建筑门宽、40 秒最长可走径对照）。
3. 正式可玩镜头参数（默认/最小/最大正交半高，以及最近镜头清晰度是否满足 4096×2731 或需另行调整密度）。
4. 出生点坐标（`spawnWorld`）、各入口/传送点位置（西南石桥口、东北牌坊门、北院支路、西北木桥等）。
5. 正式可行走区域掩码/导航网格（`walkmask.txt` / 服务端 `scene.bin`）与碰撞阻挡（布庄/器皿铺/茶馆/糕点铺、溪流/双桥/菜地/围栏/手推车，以及东侧两铺节庆局部物件与通路的一致性确认）。
6. 既美术旧账中记录的 `normal/r03_c03/s01_s04/attempt-01`（`call_7039a5250c7f4595a0eea7b94e8fbd4c`）旧 UNKNOWN 状态仍原样保留，不因客户端预览接入视作结案。

## 3. 设计分析

### 3.1 为什么复用 `PcOriginalMapPreviewViewer` 而非旧 `CityTiles4K` / `CurrentMapTileStreaming` / `FestivalMapVisualPreview`

- 旧 `CityTiles4K`（4096² 方块）和 `CurrentMapTileStreaming` / `MapDeliveryContract`（`QdaoMaps20261102`）强制要求方形 1024² 图块、正式 `formal_art_delivery` 状态、权威 `sceneId`、`walkmask.txt` 与 `spawnWorld`，不适用于尚无导航与世界坐标的矩形候选切片（末行 1024×683）。
- 旧 `FestivalMapVisualPreview` 同样硬编码 `texture.width == 1024 && texture.height == 1024` 与固定方格排布，且同步加载全部纹理。
- 现有 `PcOriginalMapCatalogContract` + `PcOriginalMapPreviewViewer` + `PcOriginalMapPreviewImporter` 已天然具备：
  - 左上角原点半开矩形像素坐标 `(x, y, width, height)` 到 Unity 2D 世界坐标（`PixelsPerUnit = 1024`，精确二进制浮点无累计误差）的映射；
  - 任意矩形画布与末行/末列余数切片支持（`1024×683` 直接按 `SpriteMeshType.FullRect` 渲染，`localScale = (1, 1, 1)`，零拉伸、零补边）；
  - 多外观 `appearances[]` 原地切换（切春节/中秋保持相机焦点与缩放不变）；
  - 符合 WebGL2 单线程与 App IL2CPP 约束的纯主线程 `Resources.LoadAsync<Texture2D>` 流式加载与逐块总览兜底；
  - `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 隔离边界，不污染正式联机逻辑。

### 3.2 最小兼容性扩展（保持全部既有地图与测试零破坏）

1. **资源隔离目录**：新增 `Assets/Resources/World/OriginalMapPreview20261124/`，与 `PcOriginalPreview20261123/`（严格 36 张端游图）及 `QdaoMaps20261102/` 物理隔离。
   - 24 张切片直接按源字节无损拷贝（与 `delivery-candidate-v001/{spring,midautumn}/manifest.json` 逐字节 SHA-256 一致，共约 64 MB，单文件 ≤ 3.3 MB）。
   - 每节派生 1 张等比 2048×1366 RGB PNG 总览兜底图（仅用于流式加载首帧兜底，元数据明确标注 `overviewDerived: true`）。
2. **契约兼容扩展**（`Assets/Scripts/World/CurrentMaps/PcOriginalMapCatalogContract.cs`）：
   - 新增原创预览目录类型常量 `ExpectedOriginalCatalogKind = "CLIENT_ORIGINAL_MAP_PREVIEW_CATALOG"`、资源根 `OriginalPreviewRoot`、默认路径 `OriginalPreviewCatalogResourcePath`，以及阶段常量 `ArtStageDeliveryCandidate = "delivery_candidate_not_formal"`（显示「原创交付候选(未验收)」）。
   - 保持 `IsValidMapId` 严格 5 位数字端游 ID 不变；新增 `IsValidOriginalMapId` 支持规范原创 ID（如 `original:guideng-market`），仅在原创预览目录校验时启用，且允许未登记服务端场景的 `numericMapId == 0` 及无 `client.dbfs` 的 `sourceContainer`。
3. **导入器兼容扩展**（`Assets/Editor/PcOriginalMapPreviewImporter.cs`）：
   - 将 `Assets/Resources/World/OriginalMapPreview20261124/` 纳入同一 `AssetPostprocessor` 管线。
   - 针对源 PNG 虽为 RGBA 色彩类型但全像素不透明（`alpha=(255,255)`）的场景：优先读取同目录 `tiles.json` 的 `"hasAlpha"` 字段；当明确为 `false` 时强制 `alphaSource = None`、`alphaIsTransparency = false`、`TextureImporterFormat.RGB24`、`TextureImporterCompression.Uncompressed`、`npotScale = None`、`mipmapEnabled = false`、`wrapMode = Clamp`、`filterMode = Bilinear`、`isReadable = false`、清空全部平台覆盖——既 100% 保留源 PNG 无损字节，又不浪费 25% 无效透明通道显存，并防止 683px 末行被 NPOT 拉伸或缩放。
4. **查看器与入口完善**（`PcOriginalMapPreviewViewer.cs`、`AppBootstrap.cs`、`FestivalMapPreviewMenu.cs`、`TianyongSandboxBootstrap.cs`）：
   - `AppBootstrap.AutoSpawn()` 增加 `FindAnyObjectByType<PcOriginalMapPreviewViewer>()?.IsStandalone == true` 守卫，防止独立预览场景启动时叠加正式登录 UI。
   - `PcOriginalMapPreviewViewer` 新增接受 `catalogPath` 的 `Open` 重载，按目录类型显示原创候选标题与状态；单图目录收起冗余搜索列表与缺口面板，突出「春节 / 中秋」外观切换按钮与 `F` 全图 / `1` 原像素按钮。
   - 新增独立预览场景 `Assets/Scenes/World/GuidengMarketPreview.unity`、菜单入口 `MMORPG > Maps > 桂灯集双节候选预览`，并在 `FestivalMaps.unity` 离线面板增加「桂灯集双节预览」入口按钮。

## 4. 思想分析

- **严格区分「可切换预览」与「正式上线交付」**：在缺乏权威世界范围、出生点和导航的前提下，任何由图片像素反推坐标或捏造 `sceneId` 的做法都会制造假一致性和后续返工；通过独立原创预览目录 + `delivery_candidate_not_formal` 阶段标记，先让项目组在真实 Unity 渲染管线下直观核验双节成图、边缘切片与镜头清晰度，再在确认坐标和导航后按正式流程入图。
- **单真相点复用**：复用经过验证的 `PcOriginalViewportPlanner` 与 `PcOriginalMapPreviewViewer` 矩形切片管线，不重复造第二套分片查看器。

## 5. 漏洞分析

1. **末行 683px NPOT 自动缩放/拉伸风险**：Unity 默认或未锁定 `npotScale = None` 时会把 683px 缩放到 512 或 1024，导致末行模糊或错位。**缓解**：导入脚本 `.meta` + `PcOriginalMapPreviewImporter` 双重锁定 `npotScale = None`、`maxTextureSize = 1024`，并在 Unity batchmode 验证脚本中断言 `r03_c01..c04` 的真实 `Texture2D.height == 683`。
2. **全不透明 RGBA PNG 误按 RGBA32 导入风险**：源图 PNG 为 4 通道 RGBA 但全像素 alpha=255，`DoesSourceTextureHaveAlpha()` 返回 `true` 会多占 25% 显存。**缓解**：`PcOriginalMapPreviewImporter` 读取目录 `tiles.json` 的 `hasAlpha` 声明（桂灯集为 `false`），导入为 `RGB24 Uncompressed`，同时保留未来含真实透明像素原创图的 `RGBA32` 能力。
3. **独立预览场景被正式 `AppBootstrap` 覆盖风险**：`AppBootstrap.AutoSpawn()` 原先只放行 `TianyongSandboxBootstrap` 和 `FestivalMapVisualPreview`。**缓解**：补齐 `PcOriginalMapPreviewViewer.IsStandalone` 放行守卫。
4. **切外观时旧异步请求或共享路径误卸载风险**：春节与中秋西侧 6 块内容一致，但物理路径各自独立位于 `spring/` 与 `midautumn/`，且 `PcOriginalMapPreviewViewer` 已带 `_mapGeneration` 与 `IsResourcePathActive` 守卫，切换及快速回切均不会误卸在用纹理。
5. **稀疏检出与换行符改写风险**：`.gitattributes` 为 `OriginalMapPreview20261124/**/*.json`、`.meta` 与验证证据 JSON 声明 `-text`，避免跨平台换行改写破坏 SHA-256。

## 6. 步骤分析与执行步骤

1. **资源导入与离线校验**：编写 `tools/import_original_map_preview.py`，核验全部整图/切片 SHA-256 与 0 像素误差无损重组，导入 24 张原字节切片、2 张总览图、清单、来源元数据与 `.meta`，并跑通离线 `--verify`。
2. **兼容代码与入口实现**：扩展 `PcOriginalMapCatalogContract.cs`、`PcOriginalMapPreviewViewer.cs`、`PcOriginalMapPreviewImporter.cs`、`AppBootstrap.cs`、`FestivalMapPreviewMenu.cs`、`TianyongSandboxBootstrap.cs`，创建 `Assets/Scenes/World/GuidengMarketPreview.unity` 及 `.gitattributes` 规则。
3. **自动化测试与 Unity 实机验证**：补充 EditMode 测试与纯 C# 契约测试，新增 `Assets/Editor/GuidengMarketPreviewVerification.cs` 在 Unity 6000.6.0f1 batchmode 下验证纹理导入设置、场景绑定、双节异步加载、外观切换与世界包围盒，并真实渲染整图、1:1 东侧商铺、最近镜头 1024↔683 末行接缝、节庆局部细节和西南桥/东北门截图。
4. **回归与人工画面核查**：跑通 Roslyn 全量编译门禁、`PcOriginalMapPreviewTests`、Python 离线校验与回归测试、Unity EditMode 测试与既有 36 图校验，并用眼睛逐张检查 Unity 实拍截图，如实记录完成项与未完成项。
