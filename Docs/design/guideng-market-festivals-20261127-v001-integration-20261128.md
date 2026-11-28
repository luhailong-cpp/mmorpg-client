# 原创地图「桂灯集」双节正式静态美术客户端预览接入设计（2026-11-28）

> 日期：2026-11-28  
> 适用仓库：`mmorpg-client`（仅客户端静态预览接入，不改写服务端配表或假造导航/碰撞）  
> 图仓来源：Azure `main` 提交 `7a3c25f9898d1bc743af5964330d5eacd304a83f`（`designs/guideng-market-festivals-20261127-v001/`，全部 45 个文件及 Git LFS 实图已逐字节 SHA-256 核验）  
> 状态：**正式静态美术交付预览接入（`formal_art_delivery`，`staticBackOnly: true`，`serverRegistrationStatus: unregistered_local_preview_only`）**

---

## 1. 需求分析

1. **完整接入最新桂灯集双节静态美术**：从图仓提交 `7a3c25f` 提取 `original:guideng-market`（桂灯集）共同中立底图、春节/中秋两张 `4096×2731` RGB PNG 整图、两节各 `4×3 = 12` 张（共 24 张）无损切片、`manifest.json`、`records/art-acceptance.json` 与 4 笔宿主内置原生记录，全部完成逐文件 SHA-256 与 0 误差整图重组校验。
2. **独立修订目录与旧候选完整保留**：按图仓接入说明，将新版本写入独立修订目录 `Assets/Resources/World/OriginalMapPreview20261124/guideng-market/festivals-20261127-v001/{spring,midautumn}/`，**旧候选目录 `delivery-candidate-v001/` 与历史证据 `Docs/VerificationEvidence/guideng-market-preview-20261124/` 原样保留、不覆盖不删除**。
3. **复用既有预览场景与外观切换机制**：更新 `Assets/Resources/World/OriginalMapPreview20261124/catalog.json` 将 `original:guideng-market` 映射至新修订目录；沿用 `Assets/Scenes/World/GuidengMarketPreview.unity` 与 `PcOriginalMapPreviewViewer`，以整图派生 `2048×1366` RGB PNG 作概览兜底、运行时按半开像素矩形加载 12 张切片，精确保留末行 `1024×683` 高度并保持春节/中秋同机位同缩放原地切换。
4. **严格守住未分配场景的边界**：本交付 `acceptanceScope = "map_art_and_pixel_coordinates"`、`numericMapId = null`（`not_assigned`）；经核实同级 C++ 服务端 `generated/tables/basescene.json`（ID 1–21）仍未分配桂灯集权威 `sceneId`、世界矩形、出生点、碰撞与导航，本次严守 `staticBackOnly: true`、`completeGameplaySceneClaimed: false`、`productionTravelEnabled: false`，不擅自推算世界坐标或写入 `QdaoMaps20261102`。

---

## 2. 设计分析

### 2.1 目录隔离与元数据合同
- **运行资源路径**：
  - 春节：`Assets/Resources/World/OriginalMapPreview20261124/guideng-market/festivals-20261127-v001/spring/`（12 张无损 RGB PNG 切片共 17.96 MB + 派生 `2048×1366` 概览图 5.12 MB + `tiles.json` + `source.json` + `.meta`）
  - 中秋：`Assets/Resources/World/OriginalMapPreview20261124/guideng-market/festivals-20261127-v001/midautumn/`（12 张无损 RGB PNG 切片共 17.96 MB + 派生 `2048×1366` 概览图 5.12 MB + `tiles.json` + `source.json` + `.meta`）
  - 旧候选保留：`Assets/Resources/World/OriginalMapPreview20261124/guideng-market/delivery-candidate-v001/` 原字节不动。
- **阶段与状态**：
  - `catalog.json` 升级 `catalogRevision = "original-preview-20261128"`、`artStageDefault = "formal_art_delivery"`、`artStage = "formal_art_delivery"`、`sourceStatus = "MAP_ART_AND_PIXEL_COORDINATES_ACCEPTED"`、`revision = "festivals-20261127-v001-spring"` / `"festivals-20261127-v001-midautumn"`。
  - `source.json` 与 `Docs/VerificationEvidence/guideng-market-festivals-20261127-v001/import-receipt.json` 完整固化图仓提交 `7a3c25f9898d1bc743af5964330d5eacd304a83f`、共同中立底图、7 份生成记录（3 份 derived 整图 + 4 份 host-builtin 原生回图，如实记录 `actualModel: null, actualQuality: null` 及配置目标快照）、13 份附加验收文件 SHA-256 与 0 像素误差重组结果。
- **东侧差异边界核实**：
  - 两节编辑多边形集中于东侧茶馆与糕点铺，避开 `r03_c03` 处手推车轮与路面保护区 `[2735, 1990, 3035, 2160]`；因此实测两节精确 5 块东侧商铺切片不同（`r01_c03`、`r01_c04`、`r02_c03`、`r02_c04`、`r03_c04`），西侧 6 块及 `r03_c03` 共 7 块逐字节一致。

### 2.2 代码最小兼容性同步
1. `tools/import_original_map_preview.py` & `tools/tests/test_import_original_map_preview.py`：
   - 适配 `designs/guideng-market-festivals-20261127-v001/manifest.json` 顶层契约，校验全部 45 份交付文件 SHA-256、整图重组、`catalog.json.meta` GUID 保留与旧候选目录完整性，并新增合成数据导入/防篡改单测。
2. `Assets/Scripts/World/CurrentMaps/PcOriginalMapCatalogContract.cs`：
   - 补齐 `ArtStageFormalDelivery` 同样禁止 `isTemporaryOriginal = true` 的契约校验。
3. `Assets/Scripts/World/Tianyong/PcOriginalMapPreviewViewer.cs` & `Assets/Editor/FestivalMapPreviewMenu.cs`：
   - 面板标题按 `artStage` 区分显示「原创地图双节预览（正式美术 · 静态预览）」与「交付候选 · 未验收」；菜单入口更新为 `MMORPG > Maps > 桂灯集双节预览`。
4. `Assets/Editor/GuidengMarketPreviewVerification.cs` & `Assets/Tests/EditMode/Tianyong/GuidengOriginalMapPreviewTests.cs` & `tools/tests/pc_original_map_preview/Program.cs`：
   - 批量验收输出迁移至独立目录 `Docs/VerificationEvidence/guideng-market-festivals-20261127-v001/`，新增旧候选保留断言、西北木桥与茶馆台阶近景视角；更新 EditMode 与纯 C# 契约单测，并同步修正 `PcOriginalMapPreviewTests` 中因 `39001` 退役残留的旧计数（`36→35` 图、`54→53` 外观、`1395→1383` 切片）。
5. `.gitattributes`：新增 `/Docs/VerificationEvidence/guideng-market-festivals-20261127-v001/**/*.json -text`。

---

## 3. 思想分析

- **单一渲染管线与修订版本隔离**：沿用已经过双形态（WebGL2 单线程异步 `Resources.LoadAsync` + App IL2CPP 零反射发射）验证的 `PcOriginalMapPreviewViewer` 矩形切片管线，通过独立修订目录平滑切换资源版本，无需新建第二套查看器或改动已有 35 张端游图与 `QdaoMaps20261102` 正式/离线走图。
- **失败关闭与诚实边界**：源交付 JSON 在 Windows 默认 `core.autocrlf=true` 下易因换行转换触发 SHA-256 失配，通过临时稀疏工作树按精确 Git LF blob 检出并逐字节校验，避免任何隐式容错；同时如实记录当前 Unity 编辑器占用项目锁导致未跑 batchmode GPU 实拍，不虚构未执行的实机证据。

---

## 4. 漏洞分析与防呆措施

1. **Windows `core.autocrlf=true` 导致校验假失败**：提取图仓元数据文件时显式指定 `-c core.autocrlf=false`，并在客户端 `.gitattributes` 为新目录与证据 JSON 锁定 `-text`。
2. **末行 `1024×683` 切片被 NPOT 缩放或平台压缩覆盖**：`PcOriginalMapPreviewImporter` 已在 Unity 6000.6.0f1 实测导入全部 24 块新切片，`.meta` 核验确认 `nPOTScale: 0`、`maxTextureSize: 1024`、`enableMipMap: 0`、`textureFormat: 3`（RGB24）、`textureCompression: 0`（Uncompressed），且 `Standalone` / `Android` / `iOS` / `WebGL` 四端覆盖均为 `overridden: 0`。
3. **误删或误改旧候选目录**：导入器强制拒绝 `--revision delivery-candidate-v001`，离线校验与 C# 单测均显式断言 `delivery-candidate-v001/{spring,midautumn}/tiles.json` 与全部 24 块旧切片仍完整存在。
4. **并发工作区隔离**：客户端仓内并发存在青岚镇、水车庭院与商业化未提交改动，操作前后均严守改动边界，零覆盖无关文件。
5. **待确认与剩余实机验证项**：
   - Unity 编辑器当前正打开 `D:\luyuan\mmorpg-client`，锁定 `Temp/UnityLockfile`，故未在命令行执行 batchmode GPU 实拍；可在已打开的 Unity 编辑器直接执行 `MMORPG > Maps > 桂灯集双节预览` 或 `MMORPG > Maps > 验收桂灯集双节预览` 生成 `unity-verification.json` 与 16 张实机镜头截图，并在目标三端（PC / 微信抖音小游戏 / iOS & Android App）复核压缩与显存表现。
   - 正式上线走图前仍需客户端与服务端共同确认权威 `sceneId`、`worldRect`、相机边界、出生/传送点、碰撞阻挡与导航网格（`walkmask.txt` / 服务端 `scene.bin`）。

---

## 5. 步骤分析

1. 在临时目录基于图仓提交 `7a3c25f` 建立稀疏分离工作树，拉取全部 45 个 Git LFS 实图并以 exact LF 检出元数据。
2. 扩展 `tools/import_original_map_preview.py` 并新增 `tools/tests/test_import_original_map_preview.py`，导入至 `festivals-20261127-v001/`，验证全部 SHA-256、0 误差整图重组与旧目录保留。
3. 同步更新 `PcOriginalMapCatalogContract.cs`、`PcOriginalMapPreviewViewer.cs`、`FestivalMapPreviewMenu.cs`、`GuidengMarketPreviewVerification.cs`、`GuidengOriginalMapPreviewTests.cs`、`tools/tests/pc_original_map_preview/Program.cs` 与 `.gitattributes`。
4. 执行离线校验、Python 单测、纯 C# 契约测试、Roslyn 全程序集编译校验（含 Runtime / Editor / EditMode），核验 Unity 6000.6.0f1 自动导入后的 `.meta` 设定并逐张目视复核导入切片拼图与 1:1 细节。
5. 落盘本设计文档并清理临时稀疏工作树。

---

## 6. 执行步骤与实测结果

1. 临时稀疏工作树拉取提交 `7a3c25f9898d1bc743af5964330d5eacd304a83f` 全部 45 份交付文件，执行 `tools/import_original_map_preview.py` 导入至 `Assets/Resources/World/OriginalMapPreview20261124/guideng-market/festivals-20261127-v001/`，输出回执 `Docs/VerificationEvidence/guideng-market-festivals-20261127-v001/import-receipt.json`（`catalogSha256 = 8f2d61693cdac33272024d4ff8e0c99452befdbb1c9cca9efe221a73cccd9eeb`）。
2. Unity 6000.6.0f1 编辑器自动完成新纹理导入（158 行 `.meta`，RGB24 Uncompressed，NPOT=None，四端零平台覆写）及域重载，随后复跑 `python tools/import_original_map_preview.py --verify` 绿灯通过。
3. 测试与编译门禁实跑结果：
   - `python tools/tests/test_import_original_map_preview.py -v`：1/1 PASS。
   - `python tools/tests/test_import_pc_original_maps.py -v`：6/6 PASS。
   - `dotnet run --project tools/tests/pc_original_map_preview/PcOriginalMapPreviewTests.csproj -- .`：15/15 PASS。
   - `pwsh -File tools/client_compile_check.ps1` + Editor / EditMode.Tianyong Roslyn 编译检查：0 错误通过。
4. 视觉复核：从已导入的 `festivals-20261127-v001/{spring,midautumn}/tiles/` 拼回整图并逐张目视检查双节总览图与 1:1 近景（东侧茶馆/糕点铺春节灯笼福字元宝金蟾与中秋桂花月兔月饼、`(2048, 2048)` 末行 `1024↔683` 十字缝、西北木桥、西南石桥与台阶），均无缝清晰、无光污染。
5. 删除临时稀疏工作树，确认图仓与客户端仓均无残留临时目录。
