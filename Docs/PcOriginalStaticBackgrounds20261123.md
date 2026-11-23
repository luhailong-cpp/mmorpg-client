# 端游原图静态背景本地预览接入说明（2026-11-23）

本接入对应交接文档 `D:/work/image/.codex-artifacts/pc-original-static-backgrounds-20261123/GEMINI_CLIENT_SERVER_INTEGRATION_HANDOFF.md`，供用户在客户端本地查看和切换 36 张已核定的端游原尺寸静态 `back` 背景，后续逐图替换为道家 Q 版改绘成品。

> **口径声明**：本批次资源仅为端游 `client.dbfs`（SHA-256 `724b3f9b1018951ac21d502a798d54b1b86d0a04250f01e1524add33525dc07f`）解出的静态背景层，**不是道家 Q 版改绘成品**，不包含动态物件、前景叠加、NPC、碰撞、导航及服务端传送入口，不进入正式联机传送列表（`QdaoMaps20261102` / `CanTravelOnline=true`）。

## 客户端打开方式

1. **独立预览场景（推荐）**：
   - 在 Unity Editor 菜单点击 **`MMORPG > Maps > 端游原图静态背景预览 (36图)`**（打开 `Assets/Scenes/World/PcOriginalMapPreview.unity`），然后点击 **Play**。
   - 默认打开 `05000 天墉城`（`10000×6774`），左侧面板列出全部 36 张就绪地图，支持按 ID / 名称筛选、点击切换、上一张/下一张（`[` / `]` 或 `PageUp` / `PageDown`）、左键/中键拖拽及 WASD 平移、滚轮缩放、`F`/`Home` 适配全图、`1` 键切换精确 **1:1 原像素缩放**、`Tab` 收起或展开面板。
   - 点击面板顶部 **`缺口/HOLD (4+2)`** 可查看 4 个未接入缺口与 2 个 HOLD 备选的只读原因说明。
2. **从既有节庆预览进入**：
   - 打开 `Assets/Scenes/World/FestivalMaps.unity` 并进入 Play，点击面板中的 **`端游原图预览 (36图)`** 即可切换进入，并可一键返回。

## 资源组织与后续替换为道家 Q 版的方式

- **目录清单**：`Assets/Resources/World/PcOriginalPreview20261123/catalog.json`（版本 `pc-original-20261123`，`artStage = "pc_original_static_back"`）。
- **单图目录**：`Assets/Resources/World/PcOriginalPreview20261123/<mapId>/pc-original-20261123/`
  - `overview.jpg` / `overview.png`：等比例总览图（最长边 `<= 2048`），切图首帧立刻显示；
  - `tiles/r{row:02d}_c{col:02d}.jpg`（33 张纯 RGB 图，JPEG q=90）或 `.png`（`15000`、`27001`、`70001` 3 张含真实透明通道的 RGBA 图，无损 RGBA PNG）：按 `1024×1024` 网格原尺寸无重采样裁切，右/下边缘保持精确余数像素尺寸，合计 **1,025 块分片 + 36 张总览图（391.12 MB）**；
  - `tiles.json` 与 `source.json`：记录逐块坐标、字节数、SHA-256 及原始无损 PNG 溯源哈希。
- **不固化临时原图路径**：C# 运行时仅通过 `PcOriginalMapCatalogContract` 按 `catalog.json` 数据驱动读取 `resourceRoot` / `artStage` / `revision`。后续某张 `mapId` 完成道家 Q 版改绘时，只需更新该条目的 `artStage = "qdao_redrawn_static_back"`、`isTemporaryOriginal = false`、`revision` 与 `resourceRoot`，无需修改查看器源码；正式上线地图仍需另走完整导航与服务端验收流程进入正式目录。
- **离线完整性复核与重导入**：
  - 离线校验（无需 `D:/work/image`）：`python tools/import_pc_original_maps.py --verify`
  - 契约与视口规划单测：`dotnet run --project tools/tests/pc_original_map_preview/PcOriginalMapPreviewTests.csproj`
  - Unity 菜单验收：`MMORPG > Maps > 验收端游原图预览 (36图)`（或 batchmode `-executeMethod MmorpgClient.Editor.CurrentMaps.PcOriginalMapPreviewVerification.RunBatchVerification`）。

## 36 张已接入本地预览清单（均未在服务端登记）

全部 36 张地图均保留原始 `mapId`、原像素宽高与精确长宽比；经核对同级服务端 `xuanming-server-mmo-cpp` 现有 `BaseScene.xlsx`（1–21）、`World.xlsx`（1–16）、`festival_maps_20261109.json`（9 图）与 `authored_maps_20261112.json`（2 图），**服务端重叠数为 0**，均标记为 `unregistered_local_preview_only`（不从 PNG 推断碰撞、导航、入口或传送）。

| `mapId` | 名称 | 原像素 (`W×H`) | 分片网格 (`1024`) | 编码 / Alpha | 客户端资源路径 (`Resources` 相对路径) | 版本 |
| --- | --- | --- | --- | --- | --- | --- |
| `05000` | 天墉城 | 10000×6774 | 10×7 (70) | RGB JPG | `World/PcOriginalPreview20261123/05000/pc-original-20261123/` | `pc-original-20261123` |
| `01007` | 地图 01007 | 10000×6163 | 10×7 (70) | RGB JPG | `World/PcOriginalPreview20261123/01007/pc-original-20261123/` | `pc-original-20261123` |
| `16001` | 地图 16001 | 1920×1286 | 2×2 (4) | RGB JPG | `World/PcOriginalPreview20261123/16001/pc-original-20261123/` | `pc-original-20261123` |
| `29000` | 地图 29000 | 4800×3600 | 5×4 (20) | RGB JPG | `World/PcOriginalPreview20261123/29000/pc-original-20261123/` | `pc-original-20261123` |
| `02000` | 揽仙镇外 | 3634×4000 | 4×4 (16) | RGB JPG | `World/PcOriginalPreview20261123/02000/pc-original-20261123/` | `pc-original-20261123` |
| `17100` | 地图 17100 | 6957×4800 | 7×5 (35) | RGB JPG | `World/PcOriginalPreview20261123/17100/pc-original-20261123/` | `pc-original-20261123` |
| `03000` | 卧龙坡 | 4400×4400 | 5×5 (25) | RGB JPG | `World/PcOriginalPreview20261123/03000/pc-original-20261123/` | `pc-original-20261123` |
| `18000` | 地图 18000 | 6400×7200 | 7×8 (56) | RGB JPG | `World/PcOriginalPreview20261123/18000/pc-original-20261123/` | `pc-original-20261123` |
| `30000` | 地图 30000 | 4800×6000 | 5×6 (30) | RGB JPG | `World/PcOriginalPreview20261123/30000/pc-original-20261123/` | `pc-original-20261123` |
| `04000` | 官道南 | 4950×5662 | 5×6 (30) | RGB JPG | `World/PcOriginalPreview20261123/04000/pc-original-20261123/` | `pc-original-20261123` |
| `19002` | 地图 19002 | 6800×4800 | 7×5 (35) | RGB JPG | `World/PcOriginalPreview20261123/19002/pc-original-20261123/` | `pc-original-20261123` |
| `33009` | 地图 33009 | 4800×3600 | 5×4 (20) | RGB JPG | `World/PcOriginalPreview20261123/33009/pc-original-20261123/` | `pc-original-20261123` |
| `07000` | 风月谷 | 6000×4970 | 6×5 (30) | RGB JPG | `World/PcOriginalPreview20261123/07000/pc-original-20261123/` | `pc-original-20261123` |
| `19003` | 地图 19003 | 1920×1080 | 2×2 (4) | RGB JPG | `World/PcOriginalPreview20261123/19003/pc-original-20261123/` | `pc-original-20261123` |
| `34000` | 地图 34000 | 6000×5000 | 6×5 (30) | RGB JPG | `World/PcOriginalPreview20261123/34000/pc-original-20261123/` | `pc-original-20261123` |
| `07001` | 地图 07001 | 6000×4970 | 6×5 (30) | RGB JPG | `World/PcOriginalPreview20261123/07001/pc-original-20261123/` | `pc-original-20261123` |
| `20000` | 地图 20000 | 4000×2600 | 4×3 (12) | RGB JPG | `World/PcOriginalPreview20261123/20000/pc-original-20261123/` | `pc-original-20261123` |
| `36000` | 地图 36000 | 10000×8000 | 10×8 (80) | RGB JPG | `World/PcOriginalPreview20261123/36000/pc-original-20261123/` | `pc-original-20261123` |
| `08000` | 轩辕庙 | 8000×6000 | 8×6 (48) | RGB JPG | `World/PcOriginalPreview20261123/08000/pc-original-20261123/` | `pc-original-20261123` |
| `21002` | 地图 21002 | 1920×1080 | 2×2 (4) | RGB JPG | `World/PcOriginalPreview20261123/21002/pc-original-20261123/` | `pc-original-20261123` |
| `38000` | 地图 38000 | 6400×4800 | 7×5 (35) | RGB JPG | `World/PcOriginalPreview20261123/38000/pc-original-20261123/` | `pc-original-20261123` |
| `08001` | 地图 08001 | 3200×2400 | 4×3 (12) | RGB JPG | `World/PcOriginalPreview20261123/08001/pc-original-20261123/` | `pc-original-20261123` |
| `22001` | 地图 22001 | 3200×3600 | 4×4 (16) | RGB JPG | `World/PcOriginalPreview20261123/22001/pc-original-20261123/` | `pc-original-20261123` |
| `39001` | 地图 39001 | 3200×2400 | 4×3 (12) | RGB JPG | `World/PcOriginalPreview20261123/39001/pc-original-20261123/` | `pc-original-20261123` |
| `09000` | 地图 09000 | 4000×3600 | 4×4 (16) | RGB JPG | `World/PcOriginalPreview20261123/09000/pc-original-20261123/` | `pc-original-20261123` |
| `23000` | 地图 23000 | 4000×5000 | 4×5 (20) | RGB JPG | `World/PcOriginalPreview20261123/23000/pc-original-20261123/` | `pc-original-20261123` |
| `39006` | 地图 39006 | 2400×1800 | 3×2 (6) | RGB JPG | `World/PcOriginalPreview20261123/39006/pc-original-20261123/` | `pc-original-20261123` |
| `23007` | 地图 23007 | 4000×5000 | 4×5 (20) | RGB JPG | `World/PcOriginalPreview20261123/23007/pc-original-20261123/` | `pc-original-20261123` |
| `40140` | 地图 40140 | 10000×6774 | 10×7 (70) | RGB JPG | `World/PcOriginalPreview20261123/40140/pc-original-20261123/` | `pc-original-20261123` |
| `12000` | 地图 12000 | 8000×8945 | 8×9 (72) | RGB JPG | `World/PcOriginalPreview20261123/12000/pc-original-20261123/` | `pc-original-20261123` |
| `27001` | 地图 27001 | 4600×2652 | 5×3 (15) | **RGBA PNG** | `World/PcOriginalPreview20261123/27001/pc-original-20261123/` | `pc-original-20261123` |
| `13000` | 地图 13000 | 8800×5852 | 9×6 (54) | RGB JPG | `World/PcOriginalPreview20261123/13000/pc-original-20261123/` | `pc-original-20261123` |
| `28002` | 地图 28002 | 3000×1800 | 3×2 (6) | RGB JPG | `World/PcOriginalPreview20261123/28002/pc-original-20261123/` | `pc-original-20261123` |
| `41081` | 地图 41081 | 4000×2600 | 4×3 (12) | RGB JPG | `World/PcOriginalPreview20261123/41081/pc-original-20261123/` | `pc-original-20261123` |
| `15000` | 地图 15000（普通 `gbits` 版） | 1920×2298 | 2×3 (6) | **RGBA PNG** | `World/PcOriginalPreview20261123/15000/pc-original-20261123/` | `pc-original-20261123` |
| `70001` | 地图 70001 | 1920×1286 | 2×2 (4) | **RGBA PNG** | `World/PcOriginalPreview20261123/70001/pc-original-20261123/` | `pc-original-20261123` |

## 单独记录：未接入的 4 个缺口 ID 与 2 个特殊 HOLD 备选

以下条目**均未标记为已完成接入**，在客户端目录中以 `integrated: false` 单独登记且不可切换加载：

### 4 个缺口 ID（`unavailableMaps`）

| `mapId` | 原因码 | 说明 |
| --- | --- | --- |
| `29005` | `NO_STATIC_BACKGROUND_BRANCH` | 审计索引中无静态 `back` 分支。 |
| `11005` | `NO_AUDITED_COMPLETE_STATIC_BACKGROUND` | `gbits` 分支仅 16/272 坐标合格，缺 256 格。 |
| `41049` | `NO_STATIC_BACKGROUND_BRANCH` | 审计索引中无静态 `back` 分支。 |
| `50024` | `NO_INFO_MAP_VIEWPORT_PROOF` | 仅有观测 4000×3000 背景候选，缺 `info.map` viewport 证明。 |

### 2 个特殊 HOLD 备选（`specialHoldCandidates`）

| `mapId` | 分支 | 原像素 | 状态 | 处理结论 |
| --- | --- | --- | --- | --- |
| `15000` | `gbits_jdhj` | 8000×8000 | `source_valid_visual_hold` | 原源存在视觉悬崖断切，保持 HOLD 不作为默认版本；默认列表仅接入已复核的 `15000` 普通 `gbits` 1920×2298 RGBA 版。 |
| `50024` | `gbits` | 4000×3000 | `viewport_unconfirmed` | 缺少 `info.map` viewport 证明，不能当作已确认运行版，保持 HOLD。 |

详细导入回执与逐图原始证据快照见 `Docs/VerificationEvidence/pc-original-static-backgrounds-20261123/`；架构与边界设计见 [pc-original-static-backgrounds-20261123.md](design/pc-original-static-backgrounds-20261123.md)。
