# 灵禾渡（`linghe-ferry` / 场景 7）双节地图全链路接入设计方案

> 日期：2026-11-28  
> 适用仓库：`mmorpg-client`（Unity 客户端）、`xuanming-server-mmo-cpp`（C++/Go 权威服）、`xuanming-server-mmo`（Java 平行服）  
> 图仓来源：Azure `main` 提交 `bff6f3b8f0dc0476a425cbbdb4b1f1a343f83b45`（`designs/world-expansion-20261027/hd36-production-20261027/linghe-ferry/continuous-hd-20261127-v002/`，全部 113 个正式交付文件已核验）

---

## 1. 需求分析

1. **双节完整资源无损接入**：将图仓提交 `bff6f3b8` 交付的灵禾渡（`original:linghe-ferry`）春节（`spring`）与中秋（`mid-autumn`）两版 `6000×7200` 连续合成图及各 `6×8 = 48` 张（共 96 张）`1024×1024` RGBA 运行切片，按原始 PNG 字节无损导入 `Assets/Resources/World/QdaoMaps20261102/linghe-ferry/linghe-ferry-20261128-v001/` Schema 2 管线，逐片锁定 SHA-256 与派生溯源。
2. **正式主世界场景登记（场景 7）**：启用既有 `World.xlsx` / `BaseScene.xlsx` 预留槽位 `id = 7`（替代临时碧渊水宫占位），将灵禾渡登记为 `key = "linghe-ferry"`、`name = "灵禾渡"`、`sceneId = 7`、`revision = "linghe-ferry-20261128-v001"`、`clientOnly = false`、`authoritativeServerRegistered = true`，并同步完成 C++/Go 与 Java 双端严格导航及 `BaseScene` 出生点契约闭环。
3. **透明边缘与双线性采样保护**：交付切片物理栅格为 `6144×8192`，有效内容 `imageRect` 为左上角起 `6000×7200`（最右列有效宽 `880 px`、最底行有效高 `32 px`，外围 Alpha 0）。客户端以 RGB24 导入切片后 Alpha 通道被丢弃，需按有效像素边界裁剪运行块渲染区域与 UV 采样，消除右缘、底缘与右下角双线性采样黑边。
4. **双形态与规范约束**：不引入阻塞线程/原生套接字/反射发射，不新增第三方依赖，完全兼容微信/抖音小游戏（WebGL2 单线程）与 iOS/Android App（IL2CPP）。

---

## 2. 设计分析

### 2.1 几何与坐标契约（Float32/Float64 零舍入精确对齐）

- **画布网格**：`6×8` 方形切片，单块 `1024×1024`，物理总栅格 `6144×8192`（单边均 ≤ `8192`，满足 `CurrentMapTileImporter` 与 `CurrentMapData.Parse` 上限），西北起行优先（`r01_c01` .. `r08_c06`）。
- **等比世界坐标映射**：
  - 交付清单给出参考比例 `17 / 300`（`6000×7200 → 340×408`）。为确保 Unity `float`、Python/C++/Java `double` 与 `MapDeliveryContract.ValidateGeometry` 零 ULP 误差一致，取单块跨度 `tileSpan = 14855 / 256 = 58.02734375`（`scale = 58.02734375 / 1024`）：
    - `worldRect = { x: 0.0, y: -56.2139892578125, width: 348.1640625, height: 464.21875 }`
    - `imageRect = { x: 0, y: 0, width: 6000, height: 7200 }`
    - `effectiveWorldRect = { x: 0.0, y: 0.0, width: 340.00396728515625, height: 408.0047607421875 }`（与旧 `340×408` 偏差 ≤ `4.8 mm`，所有边界值在 IEEE-754 Float32 与 Float64 下逐位完全一致）。
- **导航栅格与出生点**：
  - `cellSize = 1.5`，`walkmaskColumns = 233`，`walkmaskRows = 310`，总格数 `72,230`（压缩 `9,029` 字节，末字节 2 个填充位为 0）。
  - 出生点位于西南太极牌坊内侧主路岔口：像素 `(1000, 4820)` → Unity `(56.667327880859375, 0, 134.8682403564453)`，服务端 Z-up `(134.8682403564453, 56.667327880859375, 0)`。

### 2.2 双节共享导航与目视复核

- 导航计划固化于 `Docs/VerificationEvidence/linghe-ferry-20261128-v001/navigation-plan.json`：
  - `22` 组可行走道路/广场/木桥/石阶/渡口栈桥多边形与 `32` 组显式阻挡多边形，生成 `4,580` 可走格、`1` 个单连通分量。
  - `23` 处关键地标全连通：西南入村路、西南太极牌坊门洞、出生岔口、西渡口石阶与木栈桥、南岸砾石小径、西岸主路、主桥西桥头、双粮仓东/西前场（环绕木车）、西北农舍石阶、主木桥桥面、东桥头、东主屋石阶与门前月台、东岸崖边通道、东北水车坊门前与西弯道、东岸小屋踏石、东渡口石阶与木栈桥。
  - `32` 处阻挡探针严格拦截南北河道、系泊乌篷船与小舟、金黄稻田、西菜圃、太极牌坊双柱、双粮仓及东围栏与木车、主桥四角石柱与南北护栏、西渡口屋舍、东主屋与盆栽、水车与水池、东渡口凉亭与前栏杆、南岸芦苇丛、大树及右/底/右下透明填充区。
  - 已在春节与中秋原图上叠加渲染 `walkmask` 全图与 5 组局部放大图逐格目视核验，双节装饰均在建筑檐面，道路与桥面零遮挡。

### 2.3 运行块有效边界裁剪与双线性采样防黑边

- 在 `Assets/Scripts/World/CurrentMaps/CurrentMapTileStreaming.cs` 新增纯几何辅助 `TryComputeEffectiveTileQuad` 并在 `CreateQuad` 调用：
  - 按 `Manifest.EffectiveWorldRect` 裁剪每块运行切片的世界渲染矩形，彻底不绘制填充区；
  - 仅对被 `EffectiveWorldRect` 在图块内部裁切的边（如 `r*_c06` 右缘 `880 px`、`r08_c*` 底缘 `32 px`、`r08_c06` 右下角）将对应 UV 边向内收缩半纹素 `0.5f / tilePixels`，确保双线性过滤权重对 RGB24 填充区黑色像素恒为 `0`，同时保持图块内部接缝（`u=0` / `v=1`）像素级零偏移；
  - 对无填充图的内部切片（`r01_c01..r07_c05` 等）完全保持原矩形与 `[0,1]×[0,1]` UV。
- `tools/import_festival_maps.py` 在 `allowDerivedSource=True` 时放宽连续主图正方形限制，允许 `6000×7200` 等矩形连续母图（仍强制两轴 ≥ `1024` 像素、有效区 100% 不透明、填充区 100% 透明），并在 `tools/tests/test_import_festival_maps.py` 新增回归单测。

### 2.4 客户端入口、双服务端联动与测试

- 客户端新增离线走图场景 `Assets/Scenes/World/LingheFerry.unity(.meta)`、编辑器菜单 `MMORPG/Maps/灵禾渡双节走图`、`CityTravelUiRoot` 场景 7 简介、`CityTravelWindow.WorldMapV2` 场景 7 地标名「灵禾渡」、`.gitattributes` 精确字节守护及 `CurrentMapTests` / `FestivalRegionMapTests` / `CurrentMapTileStreamingTests` 回归用例。
- 服务端同步在 `xuanming-server-mmo-cpp` 烘焙场景 7 严格导航 `scene.bin`（MSET + FMASK + 双行 SHA-256）、更新 `BaseScene.xlsx` 行 7 并经官方导表器同步 `basescene.pb` 至客户端，再由 Java 仓 `ContractSync --scope current-maps` 同步契约与单测。

---

## 3. 思想分析

- **复用现有正式管线**：沿用 `QdaoMaps20261102` Schema 2 与三端 `NavDataContract::VersionedMask` 统一架构，零新增并行加载器，保持单源哈希互锁。
- **按需最小改动**：导入器仅放开矩形派生母图校验，渲染层仅在 `CurrentMapTileStreaming` 做有效边界与半纹素 UV 裁剪，不破坏既有 `CurrentMapGround.localScale == WorldRect` 契约与所有历史地图批次。

---

## 4. 漏洞分析与防呆措施

1. **RGB24 丢弃 Alpha 导致右/底/右下边缘双线性黑边**：通过 `TryComputeEffectiveTileQuad` 对裁切边内缩半纹素并裁剪世界矩形根治，并由 `EffectiveTileQuadClipsRightBottomPaddingAndInsetsOnlyPaddedBilinearEdges` 单测覆盖四类图块。
2. **矩形母图被旧正方形校验拦截**：最小放宽 `allowDerivedSource` 下 `pixels[0] == pixels[1]` 限制，保留两轴 `>= 1024` 与严格透明边校验。
3. **跨端坐标换轴与 Float32 精度漂移**：世界与有效矩形全部选用 Float32/Float64 逐位精确有理数，避免跨语言舍入偏差导致边界误判。
4. **实机渲染边界诚实声明**：本轮完成离线字节/SHA-256 校验、Roslyn 全量编译、Python/C#/C++/Go/Java 自动化验证；Unity 编辑器/真机实跑画面观感、桥头渡口镜头比例及三端显存表现仍待 Unity 实机验收。

---

## 5. 步骤分析

1. 提取并逐文件 SHA-256 校验 `bff6f3b8` 全部 113 个文件；绘制并目视核验双节共享 `navigation-plan.json`。
2. 最小扩展 `import_festival_maps.py`、`CurrentMapTileStreaming.cs` 及对应单测；冻结证据目录并导入 96 张运行切片与 2 张 `6144×8192` 总览图。
3. 补齐 `LingheFerry.unity(.meta)`、菜单、城际出行文案、`.gitattributes` 与客户端单测。
4. 烘焙 C++ 服务端场景 7 严格导航、更新 `BaseScene.xlsx` 并导表同步双端 `basescene.pb`，补齐 C++ 与 Java 服务端同步与单测。
5. 执行三仓编译、静态门禁、Python/C#/C++/Go/Java 测试与独立离线 verify。

---

## 6. 执行步骤

1. 落盘本设计文档，导入灵禾渡双节 96 块切片并运行 `import_festival_maps.py verify` 与 `test_import_festival_maps.py`。
2. 完成客户端场景、菜单、UI 文案与 `CurrentMapTests`/`FestivalRegionMapTests`/`CurrentMapTileStreamingTests`。
3. 编写并运行 `xuanming-server-mmo-cpp/tools/navmesh_baker/prepare_linghe_ferry.py`，更新 `BaseScene.xlsx` 行 7 并导出同步 `basescene.pb`。
4. 在 Java 仓执行 `ContractSync --scope current-maps`，补齐 `CurrentMapNavigation.LINGHE_FOLDER`、单测与 `PARITY.md`。
5. 串行运行全链路编译与自动化测试并汇总验证证据。
