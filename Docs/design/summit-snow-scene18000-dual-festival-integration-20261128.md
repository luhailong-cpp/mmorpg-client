# 18000 山顶雪区（`summit-snow` / 场景 18000）双节地图客户端接入设计方案

> 日期：2026-11-28  
> 适用仓库：`mmorpg-client`（Unity 客户端）  
> 图仓来源：Azure `main` 提交 `69b44b971160d97a1b4f6fddb6e42a127fcbdefa`（`designs/client18000-summit-hd8x-20261127-v001/`，春节与中秋双节 `7240×9050` 高清母图已拉取 Git LFS 实图并逐字节校验 SHA-256）

---

## 1. 需求分析

1. 承接图仓提交 `69b44b971160d97a1b4f6fddb6e42a127fcbdefa` 交付的 `pc:18000` 山顶雪区春节（`spring.png`）与中秋（`midautumn.png`）两张 `7240×9050` 连续母图（对应原端游 `6400×7200` 地图的半开区域 `[3072, 0, 5632, 3200)`，精确边长倍率 `181/64`），完成客户端与双版服务端联机正式接入。
2. 保留 `MapDeliveryContract` 的 `1024×1024` 方形运行分片与 `RGB24` 无压缩契约，同时严格通过 `effectiveWorldRect` 将玩家行走、服务端导航和相机视野裁切在 `7240×9050` 有效山顶雪区画幅内，绝不暴露右侧与底部边缘填充区。
3. 春节与中秋两版外观共用同一份经双图叠加目视核验的 `walkmask.txt`，对双节差异装饰（中秋圆台东北角玉兔月饼案台与满月背板、春节道祠门前爆竹与中秋木栏）取并集阻挡，确保切外观零卡点、零穿模。
4. 同步提供独立离线走图场景 `Assets/Scenes/World/SummitSnow.unity`（菜单 `MMORPG/Maps/山顶雪区双节走图`）与大世界正式联机传送入口（`sceneId = 18000`, `clientOnly = false`），保持小游戏（WebGL2 单线程）与 App（IL2CPP）双形态零违规。

---

## 2. 设计分析

### 2.1 几何与坐标契约（沿用 HD8x 统一比例 `25.6 px/世界单位`）

- **母图与原端游坐标映射**：
  - 原端游 `pc:18000` 完整底图 `6400×7200`，山顶雪区覆盖半开矩形 `[3072, 0, 5632, 3200)`（`2560×3200`）。
  - 高清母图 `7240×9050`（4:5，边长倍率 `181/64`），高清像素 `(x, y)` 对应原端游坐标 `(3072 + 64·x/181, 64·y/181)`。
- **客户端运行切片网格**：
  - `8×9 = 72` 块（`≤ 128` 契约上限），每块 `1024×1024` RGB PNG，完整运行画布 `8192×9216`。
  - 有效区 `imageRect = (0, 0, 7240, 9050)` 零重采样直接裁切；右侧 `952 px`（沿 `x = 7239`）与底部 `166 px`（沿 `y = 9049`）采用 edge-clamp 边缘延伸，防止双线性过滤采入黑边。回拼 `7240×9050` 有效区 RGB 字节与母图逐像素 SHA-256 一致。
  - 底图总览 `layout.png` 按精确 `1/2` 比例（Pillow `LANCZOS`）派生为 `4096×4608`（精确 `8:9`，双边 `≤ 8192`），适配 `CurrentMapTileImporter` 统一 `RGB24`、`mipmapEnabled = false`、`wrapMode = Clamp`。
- **世界空间与出生点（全二进制精确浮点，跨端零舍入漂移）**：
  - `worldRect = (0.0, 0.0, 320.0, 360.0)`（每块 1024 切片 = `40×40` 世界单位，比例 `5/128 = 0.0390625` 精确无舍入）。
  - `effectiveWorldRect = (0.0, 6.484375, 282.8125, 353.515625)`。
  - 导航栅格：`320×360`（`cellSize = 1.0`，共 `115,200` 格 = `14,400` 字节，整除 8，零尾填位）。
  - 出生点位于圆台西南宽阔雪道正中：像素 `(3800, 2820)` → Unity `(148.4375, 0.0, 249.84375)` → 服务端 Z-up `(249.84375, 148.4375, 0.0)`。
- **近景镜头下限**：与 `waterwheel-courtyard (20000)` / `map19003 (19003)` 保持相同 HD8x 密度尺度，在 `TianyongMapRuntime` 与 `TianyongSandboxBootstrap` 将 `18000u` 最小缩放设为 `15f`。

### 2.2 双节共用走图掩码

- 固化在 `Docs/VerificationEvidence/pc18000-summit-hd8-20261128/navigation-plan.json`：
  - `16` 组可行走多边形覆盖：西北浮空石阶与西北雪台、7 段盘山石阶、山顶道祠前院、上层分岔平台、圆台广场与长雪道、中层雪台、下中弯道平台、下西雪台、双石像雪池外环雪台（西/南/东三侧贯通）。
  - `11` 组并集阻挡多边形覆盖：道祠主体及门前爆竹/中秋木栏、树干与灌木、中秋圆台玉兔月饼案台及满月背板、东北雪石、雪池水面及双石像基座。左下角与雪区隔崖断开的秋径保持阻挡。
  - 共 `12,464` 可走格、`1` 个单连通分量、`24` 个关键地标全连通、`16` 个阻挡探针全阻挡；已在春节与中秋两张完整母图上叠加绿膜目视逐段复核。

---

## 3. 思想分析

- 零新增渲染器或加载管线：直接复用 `QdaoMaps20261102` Schema 2 + `CurrentMapTileStreaming` + `effectiveWorldRect` 相机/导航双裁切链路，与水车庭院（`20000`）完全同构。
- 端到端不可变哈希锁：图仓 `69b44b9` 母图 SHA-256 → 运行切片 RGB 回拼校验 → `layout.png` 内容寻址目录 → `source.json` / `tiles.json` / `navigation.json` / `walkmask.txt` → `catalog.json` → 服务端 `scene.bin` / `.mask` / `.sha256` 闭环互锁。

---

## 4. 漏洞分析

1. **右侧 `952 px` 与底部 `166 px` 边缘填充区越界**：客户端 `CurrentMapData.IsWalkable`、相机 `SetWorldBounds(EffectiveWorldRect)` 以及服务端 Recast `--effective-rect` + FMASK 均严格限制在 `(0, 6.484375, 282.8125, 353.515625)` 内，并在 `CurrentMapTests` 与服务端单测中显式断言两处填充区阻挡。
2. **双节装饰差导致中秋穿玉兔案台**：以春节与中秋两版实图叠加复核并集障碍，圆台东北角 `(4380..4980, 2120..2510)` 统一阻挡，确保切换外观后走图掩码 SHA-256 完全一致且不穿模。
3. **跨平台换行符破坏签名 JSON**：已在 `.gitattributes` 为 `/Docs/VerificationEvidence/pc18000-summit-hd8-20261128/**/*.json` 声明 `-text`。
4. **范围边界**：本包仅覆盖 `pc:18000` 山顶雪区（西北云山秋处于独立暂停）；当前离线环境完成字节/SHA 校验、编译与自动化单测，Unity 编辑器实机渲染画面与最远/最近镜头目视验收待打开 `SummitSnow.unity` 实跑确认。

---

## 5. 步骤分析

1. 从图仓 `69b44b9` 拉取并校验 `spring.png` / `midautumn.png` 实图。
2. 人工描摹并在双节原图叠加复核 `navigation-plan.json`。
3. 切分 `8×9` `1024×1024` 切片与 `4096×4608` 总览底图，回拼验真并写入 `Assets/Resources/World/QdaoMaps20261102/summit-snow/pc18000-summit-hd8-20261128/` 与 `catalog.json`。
4. 补齐 `SummitSnow.unity(.meta)`、菜单项、`CityTravelUiRoot`、`CityTravelWindow.WorldMapV2`、镜头缩放、`.gitattributes` 与三套 EditMode 测试。
5. 联动服务端烘焙 `scene.bin`、更新 `BaseScene.xlsx` / `World.xlsx` 并同步 `Assets/StreamingAssets/Tables/{basescene,world}.pb`。
6. 运行客户端编译门禁与契约/单测校验。

---

## 6. 执行步骤

1. 拉取并校验 `69b44b9` 双节母图（`spring.png` `296d487b...`、`midautumn.png` `9c80018b...`）。
2. 生成并双图叠加目视复核 `navigation-plan.json`（`12,464` 可走格、单连通分量）。
3. 导出 `144` 张 `1024×1024` 运行切片、`2` 张 `4096×4608` 底图及完整 `.meta` / 清单，回拼 RGB 逐像素一致，写入 `catalog.json` 与 `client-install-verification.json`。
4. 接入 `SummitSnow.unity`、`FestivalMapPreviewMenu.cs`、`CityTravelUiRoot.cs`、`CityTravelWindow.WorldMapV2.cs`、`TianyongMapRuntime.cs`、`TianyongSandboxBootstrap.cs`、`.gitattributes` 及 `CurrentMapTests.cs` / `CityTravelWindowTests.cs` / `FestivalRegionMapTests.cs`。
5. 待服务端导表完成后执行 `tools/sync_tables.ps1` 同步 `basescene.pb` 与 `world.pb`，并执行 `tools/client_compile_check.ps1` 与测试验真。
