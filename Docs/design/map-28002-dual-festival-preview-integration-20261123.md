# 端游地图 28002 春节／中秋 Q 版静态背景客户端接入与服务端核对设计（2026-11-23）

> 交接来源：`D:/work/image/handoffs/pc28002-gemini-20261123/{README.md,integration-inputs.json}`  
> 交付根目录：`D:/work/image/designs/client-map-qdao-20261123/28002/delivery.json`  
> 关联服务端仓：`D:/work/xuanming-server-mmo-cpp`（`main` 分支）

## 1. 需求分析

- **核心目标**：仅针对端游地图 **28002**（名称保持权威清单的“地图 28002”，不从画面猜名），将已完成交付的春节（`spring`，非春季）与中秋（`midautumn`）两套 `3000×1800` RGBA PNG Q 版静态背景接入当前客户端查看器（`PcOriginalMapPreview.unity` / `PcOriginalMapPreviewViewer`），保证双节可切换、原像素比例（5:3）与透明通道（alpha 范围 `0–254`，含 `789,978` 透明像素与 `4,610,022` 非完全不透明像素）正确。
- **范围与红线**：
  1. 保持其余 35 张地图、4 个缺口 ID（`29005`/`11005`/`41049`/`50024`）、2 个 HOLD 备选（`15000` 8000×8000、`50024` 4000×3000）及其他并行改动原封不动。
  2. 不修改 `D:/work/image` 任何美术原件或 `delivery.json`（`runtimeIntegrationVerified` 保持原值），不自动提交或推送 `image` 仓库。
  3. 保持逻辑唯一 `mapId = "28002"` / `numericMapId = 28002`，不插入重复 ID，也不改用 `28002_midautumn` 等破坏五位数字契约的 ID。
  4. 联机可行走与服务端注册须以权威配置表（`BaseScene.xlsx`、`World.xlsx`）和导航/碰撞/出生点契约为前提；实查均无 28002 登记，严禁从 3000×1800 PNG 或预览比例 `PixelsPerUnit=1024` 推断世界范围、出生点或导航，保持 `serverRegistrationStatus = "unregistered_local_preview_only"`、`productionTravelEnabled = false`。
  5. 遵循 `AGENTS.md` 双形态硬约束（微信/抖音小游戏 WebGL2 单线程 + iOS/Android App IL2CPP/AOT）：仅使用主线程 `Resources.LoadAsync`、Built-in `Sprites/Default` 透明着色器与纯值/具体 `[Serializable]` DTO，无线程/阻塞 I/O/动态 IL 生成。

## 2. 设计分析

### 2.1 1024 无损派生分片与来源分层

- 按交接推荐复用查看器的 `1024` 方形网格合同，分别从两版最终 `3000×1800` RGBA PNG 无损裁切 3 列 × 2 行共 6 块 RGBA PNG（不补边、不缩放、不转 JPG、不抹黑底、不强改 alpha、不擅自预乘）：
  - `r01_c01.png` `[0,0,1024,1024)` (`1024×1024`)、`r01_c02.png` `[1024,0,2048,1024)` (`1024×1024`)、`r01_c03.png` `[2048,0,3000,1024)` (`952×1024`)
  - `r02_c01.png` `[0,1024,1024,1800)` (`1024×776`)、`r02_c02.png` `[1024,1024,2048,1800)` (`1024×776`)、`r02_c03.png` `[2048,1024,3000,1800)` (`952×776`)
- 独立版本路径落在既有专用导入器 `PcOriginalMapPreviewImporter` 的覆盖前缀 `Assets/Resources/World/PcOriginalPreview20261123/` 下：
  - 春节：`Assets/Resources/World/PcOriginalPreview20261123/28002/qdao-spring-v002/`
  - 中秋：`Assets/Resources/World/PcOriginalPreview20261123/28002/qdao-midautumn-v002/`
  - 旧端游原图目录 `.../28002/pc-original-20261123/` 完整保留作为原图来源对照。
- 明确区分 `sourceNativeSha256` 与旧原图 SHA：
  - `sourceNativeSha256` 绑定本次采用的 Q 版整图 SHA（春节 `367aee73...` / 中秋 `c7e15bcd...`）；
  - 旧 `client.dbfs` 原图 SHA（`d4f9e5d5...`，`7,843,148` 字节）单独保留在 `pcOriginalSourceNativeSha256` / `pcOriginalSourceNativeBytes` 与 `source.json` 中，禁止以旧 SHA 冒充新图。
- `overview.png` 等比缩略至 `2048×1229` RGBA PNG，仅作为对应节庆分片未加载完成时的临时兜底，不可替代原尺寸分片。

### 2.2 纯 C# 多外观契约扩展（`PcOriginalMapCatalogContract.cs`）

- 在 `PcOriginalMapEntry` 新增可选 `defaultAppearance`、`appearances[]`（`PcOriginalMapAppearanceEntry`）及 `pcOriginalSourceNativeSha256` / `pcOriginalSourceNativeBytes`，零破坏兼容其余 35 张单外观地图。
- 顶层字段与 `defaultAppearance`（`"spring"`）保持严格一致；`ResolveAppearance(appearanceId)` 返回已校验的对应外观视图（支持 `"midautumn"` 与 `"mid-autumn"` 别名解析），`Validate()` 强拦截：重复外观 ID、重复 `resourceRoot`、`qdao_redrawn_static_back` 误标 `isTemporaryOriginal=true`、Q 版沿用旧原图 SHA、RGBA 误用非 `.png` 资源、顶层与默认外观字段漂移。

### 2.3 消除总览与 RGBA 分片叠画缺陷 + 异步切图隔离（`PcOriginalMapPreviewViewer.cs`）

- **单层渲染改造（修复旧版 `OverviewBase` 常驻叠画问题）**：
  - 旧版将整张 `OverviewBase`（`sortingOrder=0`）常驻在所有原生分片（`sortingOrder=1`）下方。对含半透明边缘与 `alpha ∈ [0,254]` 的 RGBA 图像，叠画会导致二次 alpha 混色（边缘变暗/发粗），且 `overview` 缩略图 Lanczos 边缘会从原生分片的 `alpha=0` 像素下透出重影。
  - 改为为每个分片网格构建一块精确 UV 的轻量子区域兜底四边形（`Fallback_r{row:02d}_c{col:02d}`，共用单个 `Sprites/Default` 材质 + 显式白色顶点色，零额外纹理显存）。当分片原生纹理加载完成挂载 `Tile_r{row:02d}_c{col:02d}` 后，立即 `SetActive(false)` 隐藏对应 `FallbackObject`；仅在该分片未就绪或因预算淘汰卸载时才显示同节总览兜底。由此保证每个已就绪区域严格单层绘制，不变量 `LoadedNativeTileCount + ActiveOverviewFallbackCount == TotalTileCount` 恒成立。
- **双节切换与快速切换安全**：
  - `Select(entry, appearanceId)` 先完整校验并构建目标外观候选节点后再原子提交释放旧地图；若加载或校验失败，当前外观原封不动保留。
  - 同地图切外观保留当前相机 `_focus` 与 `_zoom`，便于用户在 1:1 桥/阶梯/院坪视角下直接来回对比春节与中秋。
  - 修复快速 `spring -> midautumn -> spring` 切换时旧代次异步回调误调 `Resources.UnloadAsset` 卸载新代次共享 `Texture2D` 的隐患：卸载前检查 `IsResourcePathActive(resourcePath)`，既防止跨切泄漏又防止误卸当前活跃纹理。
  - 工具栏新增外观切换按钮（`外观：春节 / 中秋`）与透明背景底色切换（`底色：深灰 / 纯黑 / 纯白`），便于实机核查透明边缘。

### 2.4 服务端核对与双端对账（`xuanming-server-mmo-cpp`）

- 实读当前权威表源 `data/BaseScene.xlsx`（ID 1–21）、`data/World.xlsx`（ID 1–16）、`festival_maps_20261109.json`（9 图）与 `authored_maps_20261112.json`（2 图），确认 **28002 仍未在服务端注册**，缺失以下联机前提数据（保留为显式缺口）：
  1. 权威 `BaseScene.id` / `World.scene_id` 分配与像素到世界坐标换算合同；
  2. 可走区域、墙体阻挡、阶梯/桥面通行规则与双端同源 Recast/Detour `scene.bin`；
  3. 出生点坐标、传送入口/门口与 NPC 配置；
  4. 桥/墙/亭檐等独立前景遮挡层判定。
- 同步扩展 `tools/navmesh_baker/verify_pc_original_map_parity.py` 与 `data/scene_nav_bin/pc_original_static_backgrounds_20261123.json`，记录 28002 已升级为 `qdao_redrawn_static_back` 双节静态背景（含双节 SHA 与旧原图 SHA），同时严格保持 `serverBaseSceneRegistered=false`、`serverWorldRegistered=false`、`serverNavigationStatus="UNPROVEN_STATIC_BACK_ONLY"`、`productionTravelEnabled=false`。

## 3. 思想分析

- **原地数据演进，不破坏 36 图契约**：沿用既有 `PcOriginalPreview20261123` 资产与导入器架构，仅通过可选 `appearances[]` 扩展让 28002 在同一 `mapId` 下承载双节，其余 35 图零改动。
- **先证根因再修显示**：RGBA 透明图（尤其是 `alpha_max=254`）与常驻 `OverviewBase` 叠加必会产生二次混色和边缘渗漏，逐块按需隐藏兜底是从渲染管线根源消除该问题的最小完备改动。
- **诚实区分静态预览与联机场景**：未拿到真实世界范围、导航、出生点前绝不伪造配置表，双端均以机器可检验的只读对账清单锁死未上线状态。

## 4. 漏洞分析

1. **RGBA 叠画重影/边缘加厚**：已通过逐块兜底显隐（原生分片就绪即隐藏该格兜底）根治，并配黑/白/深灰三底色渲染比对验证。
2. **快速切换 `A -> B -> A` 误卸载共享贴图**：Unity `Resources.LoadAsync` 对同一路径返回同一 `Texture2D` 实例；旧代次回调若无条件 `Resources.UnloadAsset` 会把新代次正在显示的贴图卸空。已通过 `IsResourcePathActive` 守卫修复。
3. **旧原图 SHA 误充新稿**：`catalog.json`、`source.json` 与 C#/Python 校验器均强制分离 `sourceNativeSha256`（新交付整图）与 `pcOriginalSourceNativeSha256`（旧 `client.dbfs` 原图），且对 Q 版阶段禁止二者相等。
4. **稀疏检出（sparse-checkout）遗漏**：客户端仓开启非锥形 `sparse-checkout`，新增设计文档需登记入 `.git/info/sparse-checkout`；其余资产、脚本与证据路径均已在现有检出规则内。
5. **无激活 Unity 许可证导致无法本地拉起 Unity 实机窗口**：本机 `Unity 6000.6.0f1` 无有效许可证，若 batchmode 仍被许可证拦截，须如实说明已执行的 Roslyn 编译、.NET 契约单测、Python 离线与回拼验证以及基于运行切片渲染的视觉核查，不将离线渲染冒充 Unity 实机运行截图。

## 5. 步骤分析与执行步骤

1. 新增单图 Q 版双节派生工具 `tools/import_qdao_static_map.py`，扩展 `tools/import_pc_original_maps.py` 支持 `appearances[]` 校验；运行派生生成 `28002/qdao-spring-v002/` 与 `28002/qdao-midautumn-v002/`（各 6 块无损 RGBA PNG、2048×1229 RGBA 总览、`tiles.json`、`source.json`、`.meta`），逐字节证明 6 块回拼与交付整图 RGBA 完全一致，并更新 `catalog.json`、`import-receipt.json` 与 `28002-qdao-dual-festival-receipt.json`。
2. 扩展 `PcOriginalMapCatalogContract.cs`、`PcOriginalMapPreviewViewer.cs`、`PcOriginalMapPreviewImporter.cs` 与 `PcOriginalMapPreviewVerification.cs`，支持双节切换、逐块单层兜底防叠画、快速切换异步回调防误卸与三底色透明核查。
3. 补齐 `tools/tests/pc_original_map_preview/Program.cs` 与 `tools/tests/test_import_pc_original_maps.py` 正反例测试。
4. 同步服务端 `verify_pc_original_map_parity.py`、`data/scene_nav_bin/pc_original_static_backgrounds_20261123.json`、`test_pc_original_map_parity.py` 与 `docs/PROGRESS.md`。
5. 生成基于客户端运行切片的春节／中秋全景、三底色（深灰/纯黑/纯白）边缘对比与桥／阶梯／院坪 1:1 局部核查图，逐张肉眼检查透明通道、接缝与方向。
6. 执行 Roslyn 编译检查（运行时 + 编辑器脚本）、.NET 契约单测、Python 导入与服务端对账单测，以及尝试 Unity batchmode 验收（如实记录环境结果）。
