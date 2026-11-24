# 端游地图 08001 高清双节候选及已完成道家 Q 版地图批量接入设计（2026-11-24）

> 08001 交接单：`D:/work/image/designs/pc-map-08001-4x-20261124-v001/GEMINI_08001_CLIENT_HANDOFF.md`  
> 美术根目录：`D:/work/image/designs/`（只读引用，不改写任何原件或记录）  
> 关联服务端仓：`D:/work/xuanming-server-mmo-cpp`（`main` 分支）

## 1. 需求分析

- **08001 高清候选接入与对照**：将 `08001` 的春节与中秋 `6400×4800` 4x 高清候选（8×8 共 64 片 `800×600` RGBA PNG）接入当前本地静态预览加载器（`PcOriginalMapPreview.unity` / `PcOriginalMapPreviewViewer`），保证地图占地严格保持原 `3200×2400` 范围（Unity 世界 `3.125 × 2.34375` 单位），**原版 `08001/pc-original-20261123/` 及其配套文件完整保留且保持为默认外观（`defaultAppearance = "normal"`）**，验收通过前不替换；支持同机位切换对比 `原版 / 春节高清 / 中秋高清`。
- **其余已完成地图批量接入**：盘点 `D:/work/image/designs/` 下全部已验收成品，将尚未接入的 6 张完整双节图（`03000`、`07001`、`19003`、`23000`、`33009`、`41081`）与 1 张已完成春节单节图（`15000`，中秋因网络 `UNKNOWN` 阻断未接入）一并接入 `PcOriginalPreview20261123`，同时保留每张图的 `pc-original-20261123/` 原版目录并开放 `原版` 切回按钮。
- **未完成项显式排除**：`15000` 中秋（`UNKNOWN` 阻断）、`18000`（待原号核对）、`yuexi`（仅 3072×2560 预览）、`map13-taoliu` / `map19-beihai-beach`（已停工）、`41081` x4/x8 在制稿均不接入。
- **双端与双形态约束**：全部 36 张端游预览图在服务端 `BaseScene.xlsx` / `World.xlsx` / 导航清单均无登记，保持 `unregistered_local_preview_only`、`productionTravelEnabled = false`、不从 PNG 推断碰撞/导航；代码遵守小游戏 WebGL2 单线程与 App IL2CPP/AOT 规范。

## 2. 设计分析

### 2.1 复用 `PcOriginalWorldGrid` 承载同 `mapId` 多分辨率外观
- 沿用并行会话已在 `PcOriginalMapCatalogContract.cs` 与 `PcOriginalMapPreviewViewer.cs` 落地的 `PcOriginalWorldGrid`（`widthPixels`、`heightPixels`、`tileWidthPixels`、`tileHeightPixels`，`tiles.json` 为 `schemaVersion = 2`），不另造第二套高清坐标模型。
- 在 `PcOriginalMapAppearanceEntry` 补齐可选字段 `nativeWidth`、`nativeHeight`、`worldGrid`、`tileSize`、`columns`、`rows`、`tileCount`（缺省回退到父项，100% 兼容既有单外观和 `28002`）。
- 强制校验：**同一 `mapId` 下所有外观的世界像素范围 `(worldW, worldH)` 必须严格相等**，且高清外观不得低于原图分辨率或改变纵横比。
- `08001` 三套外观均锁定世界范围 `3200×2400`（`3.125 × 2.34375` Unity 单位）：
  - `normal`（默认）：`08001/pc-original-20261123/`，`3200×2400` RGB JPG，`4×3=12` 片，`schemaVersion = 1`，原封不动。
  - `spring`（候选）：`08001/qdao-spring-4x-v001/`，`6400×4800` RGBA PNG，直接原字节接入交付的 `8×8=64` 片 `800×600` PNG（每片 SHA-256 与 `08001-spring-delivery.json` 逐字节一致），`worldGrid = {3200, 2400, 400, 300}`，`schemaVersion = 2`。
  - `midautumn`（候选）：`08001/qdao-midautumn-4x-v001/`，同上。
- 视图层 `PcOriginalMapPreviewViewer` 已通过 `localScale = (tile.WorldRect.Width * 1024 / tex.width, tile.WorldRect.Height * 1024 / tex.height, 1)` 将每块 `800×600` 贴图精确缩放 `0.5×` 落入 `400×300` 原世界格；同地图切外观保留相机 `_focus` 和 `_zoom`，`1:1` 快捷键按当前外观真实像素密度缩放。

### 2.2 导入设置与防接缝策略
- 全部新资源位于 `Assets/Resources/World/PcOriginalPreview20261123/`，受 `PcOriginalMapPreviewImporter` 统一管控：
  - 分片 `maxTextureSize = 1024`（`800×600` 与 `≤1024×1024` 均 1:1 导入不缩图），总览 `maxTextureSize = 2048`；
  - `NPOTScale = None`、`isReadable = false`、`mipmapEnabled = false`、`wrapMode = Clamp`、`filterMode = Bilinear`、`sRGBTexture = true`、`textureCompression = Uncompressed`；
  - 清空 `Standalone / Android / iPhone / WebGL` 平台覆盖，防止隐式降采样或有损压缩；
  - 含真实透明通道的地图（`08001` 4x 高清 `alpha ∈ [0,254]`、`33009` `minA=0`、`15000` `minA=0`）采用无损 RGBA PNG（`RGBA32` + `alphaIsTransparency = true`），全部实测逐字节无损回拼；纯不透明地图（`03000`、`07001`、`19003`、`23000`、`41081`）按既有约定采用 `RGB24`（JPEG q=90）控制包体与显存。
- 保留既有逐块 `FallbackObject` 单层隐藏机制，解决 `08001` 4x 高清 `alpha_max = 254` 叠画变暗与透明边缘渗漏。

### 2.3 回退与正式采用路径
- `08001` 当前默认仍为 `normal`（原版 `pc-original-20261123/`），无需回退即处于安全基线。
- 验收通过后采用只需更新 `catalog.json` 中 `08001` 的 `defaultAppearance`（及顶层镜像字段）为 `spring`；任意时候回退只需改回 `normal`，`pc-original-20261123/` 永不覆盖或删除。
- 其余 7 张已验收图默认指向 `spring`，同样在面板内置 `原版` 按钮可一键切回 `pc-original-20261123/` 对照或回退。

## 3. 思想分析

- **零重复架构**：完全复用并行会话的 `PcOriginalWorldGrid` 契约与 `PcOriginalMapPreviewImporter` 管线，仅补齐单 `mapId` 内跨分辨率外观校验，不引入第二套坐标缩放概念。
- **候选隔离与一键回退**：通过 `defaultAppearance = "normal"` 将 `08001` 高清候选置于同机位可切换对照态，严格遵守“验收通过前不替换原版”。
- **诚实呈现验证边界**：区分静态回拼/离线切片渲染对照与 Unity 编辑器实机帧渲染，明确标注 08001 无服务端碰撞/导航定义。

## 4. 漏洞分析

1. **08001 高清 `alpha_max = 254` 叠画隐患**：每张 6400×4800 内部像素均为 `alpha=254`（非 255）；已由 `PcOriginalMapPreviewViewer` 逐块 `FallbackObject.SetActive(false)` 消除双层混色，并在深灰/纯黑/纯白三底色下复核透明边缘。
2. **64 片 `800×600` 与 `maxResidentTiles = 24` 预算交互**：最近镜头（`zoom=0.12`）与 1:1 高清镜头（1080p 下 `zoom≈0.264`）重叠片数 ≤12 片，全部常驻高清无回落；缩放到极远全景时中心 24 片高清、外围以同节 `2048×1536` 总览平稳兜底。
3. **跨仓库对账漂移**：客户端 `catalog.json` 更新后须同步运行服务端 `verify_pc_original_map_parity.py --write-manifest`，保持双端 36 图哈希一致。
4. **稀疏检出遗漏**：本设计文档须加入 `.git/info/sparse-checkout`。
5. **Unity 许可证/无头环境限制**：若本机 Unity batchmode 受许可证限制无法执行，须如实记录实际日志，不得用离线切片渲染冒充 Unity 实机运行截图。

## 5. 步骤分析与执行步骤

1. 核验 `D:/work/image/designs/` 全部地图完成度与源文件/切片 SHA-256，实看 `08001` 原版与 4x 高清预览。
2. 扩展 `PcOriginalMapCatalogContract.cs`、`PcOriginalMapPreviewViewer.cs`、`PcOriginalMapPreviewVerification.cs` 与 `tools/import_pc_original_maps.py`，统一支持 `PcOriginalWorldGrid` 多分辨率外观。
3. 扩展 `tools/import_qdao_static_map.py`，批量导入 `08001` 4x 高清候选（默认保留原版）及 `03000 / 07001 / 15000(春节) / 19003 / 23000 / 33009 / 41081`，生成逐图回执并通过全量离线校验。
4. 同步服务端 `verify_pc_original_map_parity.py`、`pc_original_static_backgrounds_20261123.json`、`test_pc_original_map_parity.py`，更新客户端 `Program.cs`、`test_import_pc_original_maps.py` 与 `Docs/PcOriginalStaticBackgrounds20261123.md`。
5. 生成基于客户端运行切片的 `08001` 最近镜头/接缝/三底色对照图并逐张肉眼核验；执行 Roslyn 编译检查、.NET 契约测试、Python 导入与服务端对账测试，并尝试 Unity batchmode 验收。
