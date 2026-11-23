# 端游原图静态背景接入设计（2026-11-23，36 张本地查看与切换）

## 1. 需求分析

- **核心目标**：按照交接清单 `D:/work/image/.codex-artifacts/pc-original-static-backgrounds-20261123/GEMINI_CLIENT_SERVER_INTEGRATION_HANDOFF.md`，先把 `selection.json` 中已核定的 **36 张端游原尺寸静态 `back` 背景**接入 Unity 客户端，支持用户在本地查看、缩放、平移与快速切换，便于后续逐图修复和替换为道家 Q 版高清改绘成图。
- **范围与红线**：
  1. 全部 40 个目标 ID（39 个保留端游 ID + 天墉城 `05000`）拆分为：**36 张 ready 静态背景**、**4 个缺口 ID**（`29005`、`11005`、`41049`、`50024`）以及 **2 个非默认 HOLD 备选**（`15000` 8000×8000 `gbits_jdhj` 存在视觉断崖处于 `source_valid_visual_hold`；`50024` 4000×3000 缺少 `info.map` viewport 证据处于 `viewport_unconfirmed`）。
  2. 严禁把 4 个缺口 ID 或 2 个 HOLD 备选标记为已完成接入；缺口与 HOLD 状态必须在清单、证据与预览界面中单独呈现且不可作为已就绪地图切换。
  3. 严禁把端游原图静态背景称为“道家 Q 版改绘成品”或混入正式联机传送目录（`QdaoMaps20261102/catalog.json` / `CanTravelOnline=true`）。
  4. 保持每个地图的 `mapId`、原始像素宽/高（`nativeWidth × nativeHeight`）和精确长宽比，不得拉伸或把原图路径硬编码固化到正式玩法代码中，必须支持后续按 `mapId` 平滑替换为道家 Q 版成图。
  5. 本包仅覆盖静态 `back` 背景，不含动态物件、前景叠加、NPC、碰撞、导航及服务端传送入口；不触碰 `D:/work/image` 的共享 Git HEAD、index 或 refs。

## 2. 设计分析

### 2.1 分层与数据驱动替换架构（SRP + 可替换边界）

为满足“先看端游原图、后续无缝替换为道家 Q 版图、且遵守 `AGENTS.md` SRP 与 WebGL2/App 双端单线程约束”，按三层拆分：

1. **纯 C# 契约与视口规划层（零 `UnityEngine` 依赖）**
   - `Assets/Scripts/World/CurrentMaps/PcOriginalMapCatalogContract.cs`
   - 定义不可变语义的数据模型：`PcOriginalMapCatalog`、`PcOriginalMapEntry`、`PcOriginalTileManifest`、`PcOriginalTileEntry`、`PcOriginalGapEntry`、`PcOriginalSpecialCandidateEntry` 与 `PcOriginalViewportPlanner`。
   - **路径解耦**：每个地图条目通过 `artStage`（`pc_original_static_back` / `qdao_redrawn_static_back` / `formal_art_delivery`）、`isTemporaryOriginal`、`replaceableByQdaoArt`、`revision` 与 `resourceRoot` 绑定当前显示资源；C# 业务代码不硬编码任何 `mapId -> 临时原图路径`。后续某张图完成道家 Q 版改绘后，只需更新目录中对应 `mapId` 的 `artStage`、`revision`、`resourceRoot` 与 `tiles.json` 即可原地切换，调用方零改动。
   - **强校验（Fail-Closed）**：拒绝重复 `mapId` / `numericMapId`、拒绝把 `unavailableMaps` 列入 `maps`、拒绝超纹理上限的分片（`width > tileSize || height > tileSize`）、拒绝分片像素矩形重叠或未能 100% 严密覆盖 `[0, nativeWidth) × [0, nativeHeight)`。

2. **运行时按需分片流式查看器（`#if UNITY_EDITOR || DEVELOPMENT_BUILD`）**
   - `Assets/Scripts/World/Tianyong/PcOriginalMapPreviewViewer.cs`
   - 使用统一 `PixelsPerUnit = 1024f`，整图世界尺寸精确为 `(nativeWidth / 1024f, nativeHeight / 1024f)`，天然保证与 `native.pixels` 100% 同比例。
   - **大图与显存控制**：36 图中最大为 `36000`（10000×8000，80 块 1024 分片）与 `12000`（8000×8945，72 块）；若一次同步加载全部原始分片会造成帧卡顿与 ~320 MB 单图显存峰值。采用与 `CurrentMapTileStreaming` 一致的异步租约策略：
     - 切换地图首帧先加载 `overview` 派生总览图（最长边 `<= 2048`，严格保持原像素比例），立刻无卡顿显示全图；
     - 由 `PcOriginalViewportPlanner` 每帧按正交相机视口矩形计算可见的 1024 原生分片，仅通过主线程 `Resources.LoadAsync<Texture2D>` 异步加载（符合 WebGL2 单线程与 App IL2CPP 约束，零 `Task.Run` / 零阻塞 I/O）；
     - 严格驻留预算（默认 `MaxResidentTiles = 24`，`MaxConcurrentLoads = 4`），按距视口中心优先级淘汰不可见分片并调用 `Resources.UnloadAsset`；在途 `ResourceRequest` 带有代次标记，切图或关闭时异步回调完成后立刻释放，防止跨图显存泄漏。
   - **本地交互与切换入口**：
     - 独立场景 `Assets/Scenes/World/PcOriginalMapPreview.unity`；
     - Unity 菜单 `MMORPG > Maps > 端游原图静态背景预览 (36图)`（`Assets/Editor/FestivalMapPreviewMenu.cs`）；
     - 现有 `FestivalMaps.unity`（`TianyongSandboxBootstrap`）内增加“端游原图查看器 (36图)”快捷按钮，可一键进入并返回；
     - 左侧可折叠地图列表（支持按 ID/名称搜索、上一张/下一张快捷键 `[` / `]` 或 `PageUp` / `PageDown`）、鼠标左键/中键拖拽平移、WASD/方向键平移、滚轮缩放、`Home` 适配全图、`1` 键切换精确 **1:1 原始像素倍率**，以及只读“缺口与 HOLD 清单 (4缺口 / 2特殊)”面板。

3. **资产导入、切片与离线审计管线**
   - `tools/import_pc_original_maps.py`（含 `import` 与 `--verify` 离线自检，不依赖外部 `image` 仓库即可复核已提交资源与哈希）。
   - `Assets/Editor/PcOriginalMapPreviewImporter.cs`（`AssetPostprocessor`）：自动按目录约束导入设置（`maxTextureSize = 1024`（分片）/ `2048`（总览）、`npotScale = None`、`mipmapEnabled = false`、`wrapMode = Clamp`、`filterMode = Bilinear`、`sRGBTexture = true`；RGB 保持不透明 `RGB24`，3 张含真实透明通道的 RGBA 图 `15000`/`27001`/`70001` 保留 `RGBA32` 与 `alphaIsTransparency = true`）。
   - 存放目录：`Assets/Resources/World/PcOriginalPreview20261123/`（与正式目录 `QdaoMaps20261102`、`CurrentMaps20261009` 物理隔离）。

### 2.2 分片尺寸与编码权衡

- **无重采样精确裁切**：按 `1024×1024` 网格从原尺寸 PNG 直接裁切；右边界和下边界分片保留精确原生余数尺寸（`<= 1024`），不做补黑边也不做缩放。优点：消除边界黑边双线性采样渗色、零冗余像素、拼合还原精确等于 `nativeWidth × nativeHeight`、满足 WebGL2 / 移动端 GPU 纹理上限。
- **格式选择（兼顾画质、透明通道与 GitHub 仓库体量）**：
  - 36 张原始无损 PNG 合计 **1,625.2 MB**（单图最大 `12000` 为 149.2 MB，超过 GitHub 单文件 100 MB 红线；无损 PNG 1024 切片合计约 1.6 GB，逼近 GitHub 单次推送 2 GB 包上限且会给客户端仓引入巨量临时原图历史膨胀）。
  - 经核查全部 36 张图的像素模式：
    - **33 张纯 RGB 不透明图**：端游 `client.dbfs` 原始 `back` 分块本身即为有损压缩图；分片采用高质量 `JPEG (quality=90, optimize=True)`，保持 100% 原始像素分辨率与视觉细节，整批体量大幅降至 ~200–350 MB 区间，单块仅数百 KB，远低于 GitHub 限制且显著加快拉取与 Unity 导入速度。
    - **3 张含真实 Alpha 透明通道的 RGBA 图**（`15000` 1920×2298、`27001` 4600×2652、`70001` 1920×1286，共 25 块）：采用**无损 RGBA PNG**，完整保留 `alpha ∈ [0, 255]`。
  - 每个地图的 `source.json` 与 `catalog.json` 均如实记录原始无损 PNG 的 `sourceNativeSha256`、`sourceNativeBytes`、`nativePixels` 与实际分片编码 `tileFormat`（`jpg_q90_native_1024_grid` / `png_rgba_native_1024_grid`），绝不把有损派生或总览图冒充无损原始字节。

## 3. 思想分析

- **先隔离看图，再逐步替换**：当前阶段用户的核心动作是“在客户端里把 36 张端游原图逐张打开、缩放、切换检查”，之后才分批推进道家 Q 版改绘、手绘/烘焙导航与服务端场景注册。因此不应强行把未具备导航和碰撞的 36 张图塞进 `CurrentMapCatalog` / `BaseScene.xlsx`，而应建立独立、干净、面向“原图→Q版”演进生命周期的可替换目录契约。
- **双端诚实对账**：经核对服务端仓库 `D:/work/xuanming-server-mmo-cpp` 现有 `BaseScene.xlsx`（ID 1–21）、`World.xlsx`（ID 1–16）、`festival_maps_20261109.json`（9 图）与 `authored_maps_20261112.json`（2 图），本次 40 个端游 ID **全部尚未在服务端登记**，且与现有服务端 ID 零冲突。服务端侧同步建立只读对账清单与校验测试，严禁从 PNG 伪造碰撞或导航。

## 4. 漏洞分析

1. **大图 OOM / 切图卡顿风险**：`36000`（80 块）与 `12000`（72 块）若一次性同步加载会卡顿并飙升显存。**缓解**：总览图首帧兜底 + 视口裁剪 `Resources.LoadAsync` 流式加载 + 24 块驻留硬上限 + 代次隔离卸载。
2. **RGBA 透明通道误删风险**：`15000`、`27001`、`70001` 含有真实透明区域（`alpha=(0, 255)`），若统一转 RGB JPEG 会出现黑底伪影。**缓解**：导入脚本自动检测模式与 alpha 极值，凡存在非 255 alpha 一律强制走无损 RGBA PNG 与 `RGBA32` 导入器规则，并写入单元测试门禁。
3. **临时原图路径泄漏到正式联机逻辑风险**：若将 36 图直接挂进 `QdaoMaps20261102/catalog.json`，会导致 `CityTravelUiRoot` 或测试误将其视为正式美术。**缓解**：物理目录 `PcOriginalPreview20261123`、独立 C# 契约与 `#if UNITY_EDITOR || DEVELOPMENT_BUILD` 边界隔离，且所有条目明确标记 `artStage = "pc_original_static_back"`、`isTemporaryOriginal = true`、`serverRegistrationStatus = "unregistered_local_preview_only"`。
4. **缺口 ID 与 HOLD 备选被误接入风险**：`15000` 同时存在普通版（ready）和高分版（HOLD），`50024` 同时出现在 `unavailable.json` 与 `special-candidates.json`。**缓解**：导入器和纯 C# 校验器双侧校验 `ready ∩ unavailable == ∅`、`15000` 仅采用 `ordinary-source-evidence.json`（1920×2298）、4 个缺口和 2 个 HOLD 单独登记且拒绝进入可切换分片列表。
5. **稀疏检出与换行符改写风险**：客户端仓启用了非 cone `sparse-checkout` 与跨平台检出。**缓解**：将新增路径加入工作区检出范围，在 `.gitattributes` 为本次签名 JSON/meta 声明 `-text`，在 `.gitignore` 放行新增测试 `.csproj`。

## 5. 步骤分析与执行步骤

1. **阶段 A（设计落盘）**：在客户端 `Docs/design/` 与服务端 `docs/design/` 落盘本方案。
2. **阶段 B（资产导入与切片）**：编写 `tools/import_pc_original_maps.py` 与 `tools/tests/test_import_pc_original_maps.py`，逐图校验 36 张源 PNG SHA-256/尺寸/证据清单，生成 1024 原生分片、总览图、`.meta`、`catalog.json` 及 `Docs/VerificationEvidence/pc-original-static-backgrounds-20261123/` 证据文件，并跑通离线 `--verify` 与 Python 单测。
3. **阶段 C（客户端查看器与编辑器入口）**：实现 `PcOriginalMapCatalogContract.cs`、`PcOriginalMapPreviewViewer.cs`、`PcOriginalMapPreviewImporter.cs`、`PcOriginalMapPreview.unity`、更新 `FestivalMapPreviewMenu.cs` 与 `TianyongSandboxBootstrap.cs`，编写 `tools/tests/pc_original_map_preview/PcOriginalMapPreviewTests.csproj` 测试集覆盖真实 36 图目录、视口规划与 Q 版替换契约。
4. **阶段 D（服务端对账清单与校验）**：在 `xuanming-server-mmo-cpp` 新增 `data/scene_nav_bin/pc_original_static_backgrounds_20261123.json`、`tools/navmesh_baker/verify_pc_original_map_parity.py`、`tools/navmesh_baker/test_pc_original_map_parity.py`，更新 `docs/PROGRESS.md` 与 `docs/README.md`。
5. **阶段 E（多维验收）**：
   - 运行客户端离线 Roslyn 全量编译门禁（`tools/client_compile_check.ps1`，显式指向本机 Unity 6000.6.0f1）；
   - 运行 Python 导入/校验/单测、.NET C# 契约/视口测试、既有 `MapDeliveryContract` / `MapRuntimeTransition` 回归；
   - 运行 Unity Editor batchmode 实测加载 `PcOriginalMapPreview.unity`、逐一验证全部 36 图目录/总览/分片纹理导入与尺寸、以及地图切换流程；
   - 运行服务端双端对账脚本与单测。
6. **阶段 F（提交与推送）**：核实两仓仅在 `main` 分支且 `D:/work/image` 未受影响，分别提交并推送至 `origin/main`。
