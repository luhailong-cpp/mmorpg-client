新地图交付走现有CurrentMaps加载链路的schemaVersion2；旧三图schemaVersion1/4096保持兼容。每外观总格数最多128，行列不固定；运行块1024²，最高实际原生图保留在美术来源侧并逐块记SHA。约参考总像素×2由作者核生产计划，不能解读为每轴×2或固定16×8。

2026-11-07已接入9个地图ID、15套正式节庆资源，共90块。原21003双节、28007春节、35004双节的36块及旧证据保持原字节；本次追加01001双节、01002双节、05008双节、10000双节、19004中秋、23006中秋，共54块。原批次见`VerificationEvidence/festival-maps-20261106/README.md`；本次来源、导航范围与26项Python回归及实际生产契约验证见`VerificationEvidence/festival-maps-20261107/README.md`。这些登记均为clientOnly离线交付，不代表服务端已开放或三端画面验收完成。

后续完整交付使用独立批次，不重写上述资源及证据。`Tools/import_festival_maps.py --batch-config <batch.json>` 支持显式的 `schemaVersion:1`、`revision`、`evidenceDirectory` 和 `scope`；每个 scope 条目声明字符串 `mapId`、外观、columns、rows。保留编号前导零；来源的 `midautumn` 规范化为客户端 `mid-autumn`。仅中秋交付的地图默认选择 `mid-autumn`，不会伪造普通版或春节版。

revision属于整张地图。若一个批次涉及已登记地图，它必须显式覆盖该图既有全部外观；不能只导新中秋而静默删除春节。相同数值sceneId也不能以不同key重复登记。两种冲突均在写入任何证据或资源前拒绝。

批次 `selection.json` 保留1254原生及1024运行文件身份、坐标和实际正式采用证据。运行副本可指向冻结的打包副本，但原路径与SHA仍记录在 `originalRuntime`；导入只复制已验收的运行PNG，不新增生图或原生正式计数。共享验收文件必须以本坐标和原生SHA匹配；复用格同时冻结目标采用、原来源正式验收和复用计划，不计为新原生调用。

导入与可移机验证分别执行：

```powershell
python Tools/import_festival_maps.py import --batch-config Docs/VerificationEvidence/festival-maps-20261107/batch.json --selection Docs/VerificationEvidence/festival-maps-20261107/selection.json --navigation Docs/VerificationEvidence/festival-maps-20261107/navigation-plan.json
python Tools/import_festival_maps.py verify --batch-config Docs/VerificationEvidence/festival-maps-20261107/batch.json
python Tools/import_festival_maps.py verify
```

最后一条仍独立检查原五套36块，允许catalog包含后续批次；本批验证只检查自己显式授权的完整集合。每个新批次仍需绑定实际正式拼图SHA的导航标注、同图外观相同的导航字节、完整块坐标及来源证据。新增地点保持 `clientOnly=true`，服务器场景登记与联机传送另行验收。

正式登记索引为`Assets/Resources/World/QdaoMaps20261102/catalog.json`，结构为schemaVersion2和maps数组。数组只列已经独立就绪的地图，不等待其余地图；新增地图时保留索引中仍有效的其他登记。每项包含key、name、sceneId、revision、appearances，可另设defaultAppearance和clientOnly。缺省默认外观为normal；仅节庆就绪时显式选择已交付的spring，不伪造normal。缺省clientOnly=false；本次三个外部原图编号均clientOnly=true，sceneId只作客户端本地图索引，CanTravelOnline=false，不能把它们发往尚未登记这些场景的服务端。没有固定16地图白名单，也不根据地图排列臆造服务端编号。

每个appearance包含id（normal/spring/lantern/mid-autumn）、artStatus=`formal_art_delivery`、layoutSha256和resourceRoot。defaultAppearance必须在真实交付列表中；其他外观独立追加，未交付外观无法切换。目录必须为：

```text
World/QdaoMaps20261102/{key}/{revision}/{appearance}/{layoutSha256}/
  source.json
  walkmask.txt
  layout.png
  tiles.json
  tiles/r01_c01.png ...
```

目录绑定版本和来源SHA，避免旧异步回图与新源共用同一个贴图缓存键。key/revision只用ASCII字母、数字、下划线和连字符。

`source.json`字段：

| 字段 | 合同 |
|---|---|
| schemaVersion/key/sceneId | 2，与登记一致 |
| revision/appearance/layoutSha256 | 与当前选定交付一致 |
| artStatus | 必须为formal_tiles_overview；layout.png由已正式验收图块制作总览，不能拿预览冒充 |
| layoutPixels.width/height | layout.png真实尺寸，单轴不超过8192；长宽比匹配完整worldRect |
| worldRect | 完整图格画布的x、y、width、height，y为UnityZ |
| effectiveWorldRect | 地图实际内容/相机有效范围，包含于worldRect；图格外围自然延展保持不可走 |
| cellSize/walkmaskColumns/walkmaskRows | 同源矩形导航，列行分别为ceil(worldRect宽高/cellSize)，cellSize至少0.5 |
| spawnWorld | 同源可走点，UnityXYZ坐标 |

walkmask.txt保持Base64、MSB优先、从北向南行优先编码；长度为ceil(列×行/8)，末字节填充位必须为0。导航、玩家Clamp、地面碰撞和相机读取各自当前交付范围；旧图仍使用原400×300导航包络，不因新合同改变历史坐标。

`tiles.json`字段：

| 字段 | 合同 |
|---|---|
| schemaVersion/deliveryStatus | 2/formal_runtime_tiles |
| key/sceneId/revision/appearance/layoutSha256/resourceRoot | 与source和登记一致 |
| columns/rows/tilePixels | 正整数，columns×rows≤128；tilePixels=1024 |
| worldRect/effectiveWorldRect | 与source完全一致；方形图块在世界中也保持方形 |
| imageRect | 完整分片栅格中实际内容的像素矩形，原点在西北；与effectiveWorldRect采用同一均匀比例 |
| referenceWidth/referenceHeight | 参考完整地图比例；imageRect保持比例，仅允许一边缘像素的舍入误差 |
| tiles | 完整、无重复的全部坐标；新正式交付不允许用预览补缺 |

每个tiles条目含row、column（从1开始，西北起）、resourcePath（无扩展名）、运行PNG的sha256/bytes，以及最高实际原生来源nativeSha256/nativePixels。运行1024派生不计新的原生调用。

当前接口：`CurrentMapCatalog.ReloadDeliveries()`读取已安装/导入资源中的索引；`TianyongMapRuntime.RefreshCurrentDelivery()`会比较key/revision/appearance/source身份并重建；`TrySetAppearance(string)`选择真实已交付外观。外观切换先验证新底图/正式清单，并要求导航字节与范围完全相同，失败保留当前画面。它没有实现远程下载，也不会让旧安装包自动看到工作区新文件。地图UI现从catalog动态列出地点并只显示已交付外观，支持中秋，clientOnly地点可查看景色但传送不可用。Unity菜单`MMORPG/Maps/节庆地图离线预览`打开可用正式资源与本地导航的独立场景，不登录或发送联机请求。

同场景版本刷新与外观切换会先准备候选地图、可见块、相机范围和角色恢复点；准备期间旧地图保持存活。角色与相机绑定有失败回滚，整个候选准备成功后才销毁旧地图。冷启动或服务端实际切换到其他场景后加载失败，仍会清理旧导航，避免用旧地图控制新场景角色。导航保留完整mask原点，但逐点判走与玩家Clamp使用effectiveWorldRect；不足整格的边缘单元采其实际有效交集中心，不把填充区当路，也不误封合法末列。

新运行图块导入为1024、RGB24、不读回、无mipmap、Clamp/Bilinear；新正式图块总览保留NPOT比例，最大8192。六块共享驻留及在途预算、最多两个并发请求保持原规则。实际导入尺寸、画面清晰度、峰值显存与目标端加载仍需Unity和设备验证。

可运行的窄范围验证项目为`tools/tests/map_delivery_contract/MapDeliveryContract.csproj`，链接实际生产契约源码。传入UnityManagedPath后还编译并检查实际manifest/catalog/导航与旧图兼容。它不依赖缺失的UI/TMP程序集，不重跑已知失败的全项目构建。产物应放`.codex-artifacts/map-delivery-contract/`。

切换失败与成功回归项目为`tools/tests/map_runtime_transition/MapRuntimeTransition.csproj`，直接编译真实TianyongMapRuntime源码，以轻量依赖替身执行source/layout/tiles失败、候选准备失败、绑定失败回滚、独立外观成功及不同场景/冷启动失败行为。它验证运行时控制流与状态交接，不代替Unity原生渲染、物理、网络和目标设备验收；产物放`.codex-artifacts/map-runtime-transition/`。

实现范围按Windows/WebGL/Android；未改WebGL正式网络通道，未构建、上传或部署，未新增iOS工作。

## 整图加载方案（待新图交付后执行，2026-11-11记录）

2026-11-10用户定：客户端不再按视野分片（九宫格）加载地图，改为切场景前把目标图整张加载完再切；等新图做完再改。旧三图整张8GB以上，只用1254底图又太糊，所以整图改造与新主城、八仙岛、东海渔村同批落地；在此之前运行时保持上文规则。本节只记方案，未改代码、未编译、未上真机。

**现状数据（2026-11-11核对）**

- 9图15套的layout.png与1024分片拼成的完整栅格同尺寸，全部为每世界单位10像素，最大map10000为4096×3072。`CurrentMapBuilder.Build()`已同步整张加载layout作地面，分片按视野叠在上面，不增加清晰度。两者只核了尺寸，逐像素一致未核。
- 地图贴图全平台为未压缩RGB24（`CurrentMapTileImporter`不设平台覆盖），设备上每像素3～4字节：map10000一张layout即38～50MB，另加最多6块分片。15套layout与90块分片各9437万像素，贴图数据合计约566MB。
- 旧三图（schemaVersion1）每块4096²、48～64MB，主城178块、八仙岛176块、东海渔村165块；六块预算即300～380MB。它们在Resources下全部进包：Windows包`resources.assets.resS`共52GB，旧分片约26GB。
- 服务端进场连推79（NotifyEnterScene）→21（自己）→47（视野列表），没有“客户端加载完成”回执。

**1. 出图规格**

- 每套外观运行时一张贴图，单边≤4096。按每单位10像素，地图单边≤409.6单位，map10000已到上限。
- 手机用ASTC：4x4每像素1字节（4096×3072约12.6MB），6x6约0.44字节（约5.6MB）；先用4x4看画质。每套外观手机显存估算≤16MB，切图瞬间新旧两张同时驻留约25MB。Windows内存充足可暂不改；WebGL同样不宜放未压缩RGB24，按WebGL构建选定的压缩格式设平台覆盖，落码时核对；iOS不在当前实现范围，加入时同样用ASTC。
- 保持无mipmap、不可读、Clamp/Bilinear：最远缩放（正交半高55）在1080高屏幕约每单位9.8屏幕像素，贴图与屏幕约一比一。
- 清晰度待美术定：默认缩放（半高35）在1080高屏幕需每单位约15.4像素，最近缩放（半高24）需22.5，现密度约放大1.5～2.25倍、偏软。要更清楚就得提高密度，单边会超4096，届时改为2048分片、进场全部加载（仍不按视野），预算按宽×高×每像素字节重算。

**2. 加载流程（只改客户端，协议不变）**

1. 点传送时目的地（场景+外观）已确定，立即异步预载目标贴图，与服务端交接并行；跨节点交接最长约1分钟。客户端事先不知道目标图时（首次登录、重连进场），收到79再加载。
2. 收到79后，21/47照常写入`World`实体表（它们不依赖地图）；地图暂不替换，传送遮罩保持，输入锁住。
3. 贴图就绪、导航校验通过后，在`TryInstallMap`内一帧提交：用预载贴图代替`CurrentMapDefinition.LoadTexture()`里的同步`Resources.Load`（尺寸校验保留），绑定导航、角色控制和相机，释放旧图，然后撤遮罩。现状是`CityTravelUiRoot.HandleSceneEntered`收到79就`HidePanel()`，改为以地图提交为准。
4. 过期丢弃：加载请求记下`GameClient.SceneEntryEpoch`，途中又来79时，旧请求完成后直接卸载、不提交。贴图按资源路径计引用，A→B→A快速往返时不卸在用的图。
5. 失败：服务端已切换场景时退不回旧图，重试一次，仍失败则报资源损坏，不显示半张图，并沿用现有“不同场景加载失败清理旧导航”；同图换外观失败才保留当前画面（现有行为）。
6. 一次建好：现状79时`TianyongMapRuntime.EnterScene`沿用上一张图的外观（新图没有该外观则用默认）先建一次，随后`CityTravelUiRoot`调`TrySetAppearance(所选外观)`，两者不同就整张再建一次。改为79到达且场景与预载一致时，按预载的外观一次建好。

**3. 删除清单**

- 运行时：`CurrentMapTileStreaming.cs`（368行）、`CurrentMapHexGrid.cs`（201行）、`RedBlobHexGrid.cs`（240行，连同`RedBlobHexGrid.NOTICE.txt`）、`CityTileStreaming.cs`（342行；它读的`World/CityTiles4K`已不存在，调用方始终走“无高清层直接切换”分支）；`TianyongMapInstance`里`_cityTiles`、`_currentMapTiles`的配置、逐帧更新与释放，以及`FestivalRegionMap`、`TianyongPaintedCity`、`TianyongMapRuntime`、`TianyongSandboxBootstrap`中的调用。
- 测试与工具：删`CurrentMapTileStreamingTests.cs`、`CityTileStreamingTests.cs`、`CurrentMapHexGridTests.cs`和`tools/run_city_tiles_tests.ps1`；`CurrentMapTests`、`TianyongCityNpcTests`、`FestivalRegionAutoVerify`、`tools/tests/map_delivery_contract/Program.cs`里对流式组件的引用改为断言整图。
- 新图分片：运行时几何全部来自source.json，`CurrentMapCatalog`读tiles.json只为校验。整图后去掉这段校验，1024分片与tiles.json移出Resources，作交付验收文件保留、不进包；`tools/import_festival_maps.py`与tiles.json合同同步改。只留layout后，节庆图在Android包内的贴图数据由约566MB降到约94MB（ASTC 4x4）。
- 旧三图：新主城、八仙岛、渔村按sceneId 1/2/3进catalog后（`ApplyDeliveries`按sceneId顶替旧登记），删除`Resources/World/CurrentMaps20261009`，以及`CurrentMapCatalog`的旧图登记、`CurrentMapTilesManifest`的schemaVersion1分支、`CurrentMapTileImporter`的4096分片分支。
- 文档：`Docs/CityTiles4K.md`、`Docs/CityTilePublishing.md`随代码删除或改写；本文“新运行图块导入为1024、RGB24……”一段、source.json的8192上限与tiles.json合同按落地结果改写。

**4. 预算守护与验收**

- EditMode测试遍历catalog每套外观：Android（及WebGL、iOS加入时）平台覆盖为压缩格式、单边≤4096、显存估算≤16MB。交了超标的图测试直接失败，不等真机闪退。
- 尺寸上限同改为4096：`CurrentMapCatalog`（layoutPixels≤8192）、`tools/import_festival_maps.py`（layout 8192与网格`max(columns, rows) * 1024 <= 8192`）、`CurrentMapTileImporter`（8192）。
- `tools/tests/map_runtime_transition`补预载、过期丢弃、失败重试与遮罩撤除时机的用例。
- 验证交Codex：Unity编译与EditMode；真机用一台低端Android，以Memory Profiler看切图峰值，记录从收到79到地图提交的耗时；WebGL记一次峰值。720高的低端屏在最远缩放时贴图约缩小1.5倍，无mipmap可能轻微闪烁，真机顺带看。
- 落码前用脚本核一次layout与分片逐像素一致（按上文artStatus，layout应由已验收分片拼成）。

**5. 跨仓影响**

- 协议不变，服务端与Java版不改。只有将来加载要数秒、或进场即可能受击时，才考虑加“客户端加载完成”回执与进场保护。
- 服务端`tools/navmesh_baker/sync_festival_maps.py`钉了冻结交接文件`Docs/VerificationEvidence/festival-maps-20261107/server-handoff-data.json`的SHA与计数（含`runtimeTiles=90`），并按路径与SHA校验每套的source.json、tiles.json、navigation.json、walkmask.txt。新图批次或tiles.json移动时要出新交接文件，服务端同批更新脚本并重烘导航。
- 旧三图被替换时，服务端`data/scene_nav_bin/current_maps`（绑定旧layoutSha256）按新walkmask同批重烘。
