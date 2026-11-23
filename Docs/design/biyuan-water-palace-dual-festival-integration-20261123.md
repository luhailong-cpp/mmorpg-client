# 碧渊水宫本体（`biyuan-water-palace`）双节客户端正式接入设计方案

> 日期：2026-11-23  
> 适用仓库：`mmorpg-client`（关联服务端 `xuanming-server-mmo`）  
> 交接源：`D:/work/image/.codex-artifacts/biyuan-midautumn-succession-20261123-01a206f6313d/gemini-client-integration-handoff-v001/`

---

## 1. 需求分析

1. **双节正式运行资源接入**：将美术已验收交付的碧渊水宫本体（`biyuan-water-palace`）春节（`spring`）20 张与中秋（`mid-autumn`）20 张、共计 40 张 `1024×1024` 运行切片，按原始字节无损接入客户端 `Assets/Resources/World/QdaoMaps20261102/` 正式目录并逐块校验 SHA-256。
2. **严格身份隔离**：碧渊本体与端游 `26000` 为两张不同地图，严禁合并身份或修改 `26000` 既有资源、预览与手绘导航。
3. **独立场景编号与可行走双端一致**：为碧渊本体核定独立非零 `sceneId = 5`（占用 `World.xlsx` / `BaseScene.xlsx` 中既有未占用占位槽位 `5`，不碰 `1..4`、`17..21`、`1001..35004`、`26000/26008`），统一世界映射、有效镜头边界、出生点、双节共用导航掩码与离线走图场景入口。
4. **导入工具本体适配与派生溯源保真**：扩展 `tools/import_festival_maps.py` 支持字符串本体 `key`、显式 `sceneId`、`name`、`clientOnly` 与真实来源分类（保留中秋 5 张派生图 `r01_c03`/`r03_c02`/`r03_c03`/`r04_c02`/`r04_c03` 及 `r04_c03` 1264×1264 来源尺寸的真实标注，不伪标为原生）。
5. **双形态与规范约束**：不引入阻塞线程/原生套接字/反射生成；资源与代码同时满足微信/抖音小游戏（WebGL2 单线程）与 iOS/Android App（IL2CPP）兼容红线；增量提交不得干扰并行会话中的其他未提交改动。

## 2. 设计分析

### 2.1 几何与坐标合同
- **画布网格**：5 列 × 4 行，`r01_c01` 在西北（左上），逐行向南、逐列向东；单块 `1024×1024`，完整画布 `5120×4096`（比例 `5:4`）。
- **有效场景矩形**：像素 `imageRect = (0, 128, 5120, 3840)`（比例 `4:3`，参考图 `3200×2400` 严格零误差对齐）；上下各 `128px` 仅作画面衬边，禁止纳入行走区与镜头有效区。
- **世界空间等比缩放**：取精确二次幂比例 `16 px = 1 world unit`（`scale = 0.0625`）：
  - `worldRect = (0, 0, 320, 256)`（每块切片精确 `64×64` 世界单位，无拉伸误差）
  - `effectiveWorldRect = (0, 8, 320, 240)`（上下各 `8` 世界单位对应 `128px` 不可走衬边；与规划 `340×255` 比例一致且所有边界、切片与网格均为精确整数）
  - `cellSize = 1.0`，`walkmaskColumns = 320`，`walkmaskRows = 256`，总计 `81,920` 格 = `10,240` 字节（整除 8，零尾部填充位）
- **权威出生点**：双灯门楼正中宽阔石道，像素 `(3560, 3160)` → Unity 世界 `(222.5, 0, 58.5)` → 服务端 Z-up `(58.5, 222.5, 0)`。

### 2.2 双节共用导航与关键地标
- 导航计划固化在 `Docs/VerificationEvidence/biyuan-body-20261123/navigation-plan.json`，由人工比对春节与中秋最终拼图后绘制，取两节石阶/桥面/庭院/连廊的交集：
  - 共计 `9,093` 可走格，`1` 个单连通分量，出生点与 16 个关键地标（南拱桥、门楼、直桥交汇、太极中庭、主殿阶下、主殿门前、中廊、北廊、钟鼓阶、钟鼓亭前、东拱桥、东廊、东南观景尽头、西廊、南中廊、茶亭前院）全部可达。
  - 22 个阻挡探针覆盖上下 128px 衬边、南北荷花池、东廊边水岸、东西外湖、主殿/钟鼓/茶亭屋顶及茶亭东檐、中庭双柱、双灯楼柱、东南护栏尽头、春节鞭炮/算盘/西鼓、中秋花丛以及南桥双节偏移外侧。
  - **关键双节差异处理**：春节与中秋在门楼以南的南拱桥处走向存在约 200px 东西偏移，导航只开放双节桥面公共重叠区（`y ≤ 3580`），确保两节切换时角色绝不踩上护栏或水面；东南观景台保持尽头不抄近路。

### 2.3 目录登记与入口设计
- **正式目录**（`Assets/Resources/World/QdaoMaps20261102/catalog.json`）：
  - `key`: `"biyuan-water-palace"`，`sceneId`: `5`，`name`: `"碧渊水宫"`，`revision`: `"biyuan-body-20261123"`
  - `clientOnly`: `false`（与服务端场景 5 的 `BaseScene.xlsx`、`World.xlsx` 及同源 `scene.bin` 同步落盘）
  - `defaultAppearance`: `"spring"`，`appearances`: `["spring", "mid-autumn"]`
- **导入器扩展**（`tools/import_festival_maps.py`）：
  - 向后兼容全部既有数值地图批次和单测；
  - 批次配置 `scope` 项支持可选 `key`、`sceneId`、`name`、`clientOnly`、`authoritativeServerRegistered`、`allowDerivedSource`；
  - 启用 `allowDerivedSource=true` 时，校验并如实落盘每块图块的 `sourceNative`、`sourceDerived` 及实际 `nativePixels`（含 `r04_c03` 的 `1264`）。
- **离线走图场景与菜单**：新增 `Assets/Scenes/World/BiyuanBody.unity`（复用 `TianyongSandboxBootstrap`，锁定 `deliveredMapId = 5`、`deliveredAppearance = "spring"`、`lockDeliveredMap = 1`），在 `FestivalMapPreviewMenu.cs` 注册 `MMORPG > Maps > 碧渊水宫双节本体走图`；同时在 `CityTravelUiRoot.cs` 为场景 5 补充地点简介，在 `CurrentMapTests.cs` 增加碧渊双节共用导航、切片尺寸/哈希、在线可传与阻挡/路径断言。

## 3. 思想分析

- **单一事实源与失败关闭（Fail-Closed）**：运行图块、总览图、来源清单、导航位图、服务端 Recast 网格和表数据全部通过 SHA-256 链式绑定；任一环节字节或尺寸不符均拒绝加载而非静默回退。
- **最小增量适配**：不另造第二套地图加载器，直接复用 `MapDeliveryContract` / `CurrentMapCatalog` / `CurrentMapBuilder` / `CurrentMapTileStreaming` / `TianyongSandboxBootstrap` 既有生产管线；仅对 `import_festival_maps.py` 做向后兼容的最小扩展。
- **双节同导航、视觉解耦**：切换 `spring` / `mid-autumn` 只替换材质与分片流式清单，导航位图逐字节相同，切换时不重置角色脚点或破坏寻路。

## 4. 漏洞分析与防呆措施

1. **南拱桥双节偏移踩水风险**：若只照春节图画南桥，切到中秋后角色会走到桥西水面。已通过叠图比对取双节公共桥面交集并用 `southBridgeDivergentWest` 阻挡探针回归锁定。
2. **中庭/主殿/茶亭季节摆件冲突**：春节算盘、年画屏风、西/东鼓、鞭炮礼盒与中秋玉兔、月牌、花丛位置不同。已在导航计划中合并排除两节全部摆件占位。
3. **派生图伪标原生风险**：中秋 5 张图（`r01_c03`、`r03_c02`、`r03_c03`、`r04_c02`、`r04_c03`）为派生源，且 `r04_c03` 源图为 `1264×1264`。导入器及证据文件必须保留 `sourceNative=false`、`sourceDerived=true`、`nativePixels=1264`，严禁覆盖为 `1254 native`。
4. **稀疏检出（sparse-checkout）遗漏风险**：客户端本地仓库启用了非锥形 `sparse-checkout`，新增场景和证据目录须登记进 `.git/info/sparse-checkout` 后再校验与暂存，避免提交缺失或冲突。
5. **并行会话隔离风险**：工作区存在其他任务的未提交 `Appearance*` 改动，暂存与提交必须逐路径显式指定，绝不 `git add -A`。
6. **无激活 Unity 许可证的环境限制**：本机 Unity 6000.6.0f1 日志显示无有效许可证，无法在当前机器启动带图形帧缓冲的 Unity 播放器窗口抓实机游戏截图。必须如实区分“托管生产代码 + 双节叠图/寻路轨迹视觉渲染验证”与“Unity 实机渲染帧验收”，绝不伪造 Unity 截图。

## 5. 步骤分析

1. 冻结双节 40 张运行切片选择清单 `selection.json`、批次配置 `batch.json` 与经视觉叠加复核的 `navigation-plan.json`。
2. 最小扩展 `tools/import_festival_maps.py` 及 `tools/tests/test_import_festival_maps.py`，确保原有单测与新增本体派生单测全绿。
3. 执行导入与离线独立核验（`import` + `verify`），写入 40 张字节一致的 1024 PNG、总览 `layout.png`、`source.json`、`tiles.json`、`navigation.json`、`walkmask.txt`、`.meta` 与 `catalog.json`。
4. 添加 `BiyuanBody.unity`、`FestivalMapPreviewMenu.cs` 菜单项、`CityTravelUiRoot.cs` 描述、`.gitattributes` 字节保护及 `CurrentMapTests.cs` 回归用例；同步服务端导出的 `Assets/StreamingAssets/Tables/basescene.pb`。
5. 运行离线 Roslyn 编译体检（`client_compile_check.ps1`）、Python 导入/导航单测、托管生产运行时契约/寻路验证（`FestivalMapDelivery`、`MapDeliveryContract`、`MapRuntimeTransition`）及双节路径与镜头边界可视化验证图。

## 6. 执行步骤

1. 落地本设计文档与服务端设计文档。
2. 编写 `tools/import_festival_maps.py` 本体扩展与单测并运行通过。
3. 生成 `Docs/VerificationEvidence/biyuan-body-20261123/{batch.json,selection.json,navigation-plan.json}` 并执行 `import_festival_maps.py import` 与 `verify`。
4. 新增 `Assets/Scenes/World/BiyuanBody.unity(.meta)`、更新 `FestivalMapPreviewMenu.cs`、`CityTravelUiRoot.cs`、`CurrentMapTests.cs`、`.gitattributes` 与 `StreamingAssets/Tables/basescene.pb`。
5. 执行编译门禁、全量 Python/C# 托管测试与双节视觉验证图渲染，核查全部结果后仅暂存本次改动并提交推送。
