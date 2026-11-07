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
