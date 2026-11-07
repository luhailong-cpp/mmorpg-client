# 另一台电脑 Codex：服务器地图接入接续提示词

本文件只交接后续实施任务。当前已完成的是客户端资源接入与便携校验；本轮没有注册服务器场景、发布服务器导航或部署服务，也没有解除任何 `clientOnly`。建议随客户端发布为 `Docs/ServerFestivalMapsHandoff20261107.md`。

## 可直接复制的接续提示词

```text
继续完成节庆地图的服务器接入。先读取本机两个仓库的实际 AGENTS.md；服务器再按其启动门禁读取 PROGRESS.md、.github/copilot-instructions.md、任务相关设计和当前改动。保留已有未提交工作，不按交接快照强制覆盖或重置。

客户端以包含 Docs/ServerFestivalMapsHandoff20261107.md 和 Docs/VerificationEvidence/festival-maps-20261107/server-handoff-data.json 的实际已发布 main 为准，记录真实客户端提交。ae1b136f85fb94ac402581b90e9cd30bf2f29d8c 是已推送的美术资源基线，不是尚未生成的最终交接文档提交。先读客户端 README.md、Docs/QdaoMapDeliveries20261102.md、两个 festival-maps-20261106/20261107 的 README，再读 server-handoff-data.json；所有坐标、资源路径与 SHA 均从实际文件取值。

本批固定9个外部图ID、15套节庆外观、90个1024运行块：01001双节各2；01002双节各2；05008双节各6；10000双节各12；19004仅中秋6；21003双节各9；23006仅中秋4；28007仅春节6；35004双节各6。不要把其他已返候选或局部新增正式格加入本批，13001/c03不在其中。保留带前导零的地图ID与map key；外观字符串为spring/mid-autumn。未交付普通版、春节或中秋不得伪造。

目前全部9个ID显式 clientOnly=true，CanTravelOnline=false；离线预览与本地可走不等于服务器已接受。保留这个门禁，不能删除字段利用默认false，也不能改CanTravelOnline绕过。source/selection中的serverRegistered=false或authoritativeServerRegistered=false是冻结交付当时的事实，不靠改写来源证据伪装成已部署；后续真实激活证据单独登记。

实际服务器仓库目录名是 xuanming-server-mmo-cpp，本机曾在 D:/work/xuanming-server-mmo-cpp；不要使用不存在的无-cpp目录，也不要沿客户端旧README默认../mmorpg选错仓库。服务器只读交接快照为main提交26ceb70ca5771c5ab055c2ec7144c1e55f32c528，origin为https://github.com/luyuan-cpp/xuanming-server-mmo。这不保证异机已经拥有同一提交或本机dirty内容：PROGRESS.md、cpp/libs/services/scene/spatial/constants/nav.h，以及未跟踪的docs/design/world-expansion-speed2x-20261101.json当时尚未提交，不能假装包含在该HEAD。nav.h提交内速度上限10、工作树20，须按实际交付和原要求保留、核对，不擅自清理或把其中一份当最新已提交状态。

同批交接会单独携带这3项既有工作的限定patch、未跟踪JSON原字节及SHA清单；它们不会随pull自动出现，也不是服务器已经push的回执。对应客户端证据目录里的server-existing-map-changes.patch和server-existing-world-expansion-speed2x-20261101.json，先核补丁基线26ceb70ca5771c5ab055c2ec7144c1e55f32c528及所列文件SHA，在同一基线上先做git apply --check，再按实际授权迁入。异机HEAD不同、目标已有改动或check不通过时，保留两边内容并人工合并，不强制reset、不自动套不同HEAD。原字节JSON迁移后再核SHA；这一步只恢复预存工作，不等于9图服务器接入已经完成。

先建立9图外部ID、客户端numeric sceneId、服务器BaseScene模板ID的明确对应，检查与已有场景和World配置冲突。proto/scene/scene_info.proto中的scene_config_id是uint32模板号，scene_id是uint64运行实例Snowflake，二者不可互换。当前表快照只有BaseScene 1..21、World 1..16，没有这9个目标登记；不能把客户端本地索引当已注册服务器模板，也不要新造EnterScene/传送协议或改生成代码凑接口。

逐图按server-handoff-data.json与原source.json的worldRect/effectiveWorldRect/imageRect/spawn校对坐标、裁切、比例及导航范围。客户端Unity X/Z、图像西北原点、北到南行序、MSB位序和半开边界必须一致；当前批次为0.1世界单位/运行像素、2世界单位导航格，以数据文件逐图实算为准。服务器采用Z-up；沿已有NavQuery唯一适配点核对Unity(x,y,z)到服务器(z,x,y)，不能在导出、装载、查询或网络层重复换轴。不要把客户端Base64位图直接改名当服务器MSET导航文件。

现有baker入口固定150×150，解码mask要求恰好2813字节，不能直接吞本批不同宽高的矩形mask。按实际宽高、原点、cellSize和半开边界实现并验证适配，不截断、补零或缩放强塞旧入口；边缘格取与effectiveWorldRect交集的中心，最后行列不能用imageHeight-2简单倒推。baker与服务器运行端必须使用同一ue5navmesh版本/ABI及dtReal=double，MSET由同源真实工具生成并由真实运行端验收。

保留具体限制：19004中秋只有48个已确认安全导航格，其他林区路网关闭，不能因6块美术完整就开放全林地；10000两节均为两个断开的平台域，不能穿龙体、跨云海或造隐形桥，实际色差和画面接缝仍是CLIENT_VISUAL_PENDING，不得改成整图视觉PASS；35004本来有三个不连通平台域，同样不能自动连桥。所有地图按数据中实际components保留连通性，只使用已交付mask和有效内容范围；同图双节必须保持相同导航字节。

先在隔离开发环境完成权威导航适配、BaseScene/World配置与导出，再做真实双端联调。服务器当前装载MSET会跳过缺失/损坏文件，但已有调用方存在无导航fail-open路径；新9图必须证明缺失、坏文件、越界、填充区、障碍和跨不连通域的移动被拒绝，不能只以启动没有报错或navmesh没注册为通过。核出生点及恢复点、进出场、正常与非法移动、同区/跨区传送、失败回滚、重连，以及旧场景回归。客户端托管几何/寻路和源码编译不是服务端权威导航、Unity碰撞、三端画面或真实传送验收。

配置事实源为data/schema/basescene_table.proto、data/BaseScene.xlsx及World.xlsx/schema；按data/AGENTS.md先做沙箱导出比较，再运行正式生成。真实导出配置是tools/data_table_exporter/exporter_config.yaml，会写C++、Go、Java和客户端C#目录，执行前逐项核本机真实目标，不能照搬旧机器路径。客户端proto与MessageIds必须从服务器权威proto/message_id.txt及导表生成tip再生，两个生成脚本都显式传-ProtoRoot，禁止手改Generated或MessageIds。若改客户端可见契约/配置，遵守服务器AGENTS的双版本与PARITY要求；Java仓库缺失则记为待迁移/待对齐，不能宣称双版本完成。

先准备并验证服务器、客户端两侧变更，再把解除指定地图clientOnly的改动纳入同一已验收批次，记录成对的服务器/客户端提交、配置表导出SHA、导航文件SHA、协议/消息号来源及验证结果。未过门槛时门禁保持true；不能一边合入开放、一边仍是旧服务器。两个独立仓库用同一发布清单锁定配对版本，回滚也按配对版本执行。本任务不授权生产部署、服务重启、清库、压测清理或更改凭据；这些动作另有明确授权才执行。

每完成一项记录真实文件、命令、退出码和证据路径；未运行、缺依赖、未迁移或未验收写明。不要重新生图、重编码已审阅拼图、重验未变的美术PASS，或因旧D盘原生路径在新机不存在而重造资源。最终交付配套代码/数据、验证报告、待修项和双端版本清单；不把交接文档存在当作服务器接入完成。
```

## 跨机环境与先行校验

客户端仓库应完整取得已发布的正式资源与冻结文本证据。`server-handoff-data.json`提供每图的导航payload、encoded/packed SHA、来源和相对路径；当前数据从资源提交 `ae1b136f85fb94ac402581b90e9cd30bf2f29d8c` 的真实Git blob提取，schemaVersion为1，SHA256为 `8c8678bf19ad568feda870834e23ea8fa7ffc2d51fb7ee90139c87e970bf0b51`。它列出64个 `requiredClientFiles`（相对路径、SHA、大小、archivePath/role），便携包中的客户端文件位于 `client/` 加该相对路径。清单与数据均不包含PNG，也不替代客户端仓库中90块运行资源。便携校验不依赖美术生产目录；1254原生只作为来源身份保留，不是服务器需要再生的文件。

本机已使用的环境：Unity `6000.6.0f1 (f7f8ed4d1e24)`；`com.unity.test-framework 1.8.0`、`com.unity.ugui 2.6.0`以仓库锁文件为准；.NET SDK `10.0.401`（窄验证项目目标 `net10.0`）；Python `3.12.14`、Pillow `13.0.0`；PowerShell 7。新机按项目说明恢复真实依赖，不复制 `Library` 或宿主工具缓存作为已验收环境。

服务器环境需独立按其文档恢复。本机只读快照中当前shell的PATH未找到 `cmake/msbuild/go/protoc/mvn/java`，`baker.exe`和指定的Detour/protobuf头文件也未就绪；这不是可构建或已部署的证明。Java兄弟仓库当时也未在声明路径找到。不要为了运行交接而执行压测文档里的清库流程。

实际版本来源以代码为准：`go/scene_manager/go.mod`声明 Go `1.24.5`，不能沿旧README的Go1.21最低值；`tools/data_table_exporter/requirements.txt`声明 `openpyxl>=3.1.0`、`PyYAML>=6.0`、`protobuf>=7.35.1,<8`；`dev.bat`有 `libprotoc 35.1` 门禁。详见同批 `server-integration-audit.json` 的 `commandsForNextComputer` 和源码证据，里面的命令在本次交接中均未执行。

在客户端根先执行只读资源验证；路径按新机实际目录填写：

```powershell
$ClientRoot = '<新机客户端仓库>'
$ServerRoot = '<新机xuanming-server-mmo-cpp仓库>'
$UnityManagedPath = '<Unity6000.6.0f1>/Editor/Data/Managed/UnityEngine'
Set-Location $ClientRoot
python tools/import_festival_maps.py verify --batch-config Docs/VerificationEvidence/festival-maps-20261107/batch.json
python tools/import_festival_maps.py verify
dotnet run --project tools/tests/festival_map_delivery/FestivalMapDelivery.csproj --configuration Release --artifacts-path "$ClientRoot/.codex-artifacts/festival-server-handoff/managed-build" "-p:UnityManagedPath=$UnityManagedPath" -- $ClientRoot "$ClientRoot/.codex-artifacts/festival-server-handoff/managed-result.json"
```

前一条核新10套54块，后一条核旧5套36块；catalog允许二者并存。随后按仓库窄验证项目说明以实际 `UnityManagedPath`运行生产契约/导航检查，验证总9/15/90，产物放客户端 `.codex-artifacts/`。这不启动Unity或服务器。

只有服务器权威协议/表生成已经完成且确有同步需求时，才运行：

```powershell
pwsh -File tools/gen_proto.ps1 -ProtoRoot $ServerRoot
pwsh -File tools/gen_messageids.ps1 -ProtoRoot $ServerRoot
```

不要使用脚本旧默认的兄弟 `../mmorpg`。生成会写客户端代码，执行前检查本机既有修改与输出目录；不把这两条当只读资源核验。

## 接入及解锁门槛

| 门槛 | 必须保留的通过证据 |
| --- | --- |
| 交接身份 | 实际已发布客户端提交、实际服务器提交/dirty清单；预存patch基线check及JSON原字节SHA；交接JSON与相对文件SHA匹配；9ID/15外观/90块，旧36块保持原字节。 |
| 场景模板 | 9图与BaseScene/World的明确映射、外键/冲突检查、沙箱导出比较、生成器与相关构建结果；模板ID和运行实例ID未混用。 |
| 权威导航 | 正确MSET或真实服务器支持格式的可复现生成及SHA；唯一换轴；出生与恢复点；有效边界与障碍；缺/坏导航拒绝；客户端采样与服务器实际查询逐点/路径对照。 |
| 具体限制 | 19004仅48安全格、未开放林网；10000两域不可达测试和视觉待修状态保留；35004三个独立平台及其他图实际components不被擅自连通；双节导航SHA一致，填充区不可走。 |
| 双端实测 | 隔离环境中真实登录、入场、正常/非法移动、传送、失败/超时/断线回滚、重连与旧地图回归；相应Unity/目标设备验证另记，不用源码编译替代。 |
| 同批激活 | 服务端场景/导航与客户端门禁变更均已准备并通过；同一清单锁定双端提交和数据SHA，才提交指定解锁；保持可回滚，不单边开放或直接部署生产。 |

当前六项都不能仅凭本文件标为已通过。本轮交付保持服务器未注册/未部署、9图 `clientOnly=true`；后续接手者只按自己实际完成的证据逐项更新实施报告。

## 同批文件与校验引用

以下均发布到客户端 `Docs/VerificationEvidence/festival-maps-20261107/`，文件名大小写按实际Git路径使用；表中SHA绑定当前冻结原字节，不能以重编码或编辑文件再声称匹配。

| 相对文件名 | SHA256 | 用途 |
| --- | --- | --- |
| `server-handoff-data.json` | `8c8678bf19ad568feda870834e23ea8fa7ffc2d51fb7ee90139c87e970bf0b51` | 9图客户端合同、导航payload和64份必要文本/代码引用；没有PNG。 |
| `server-integration-audit.json` | `2ec0ea1303ad9ff0336fd75d7fe90b7cf707ea4a452edfdf269536f79d9bd933` | 服务器真实源码、37处SHA证据、配置/导航缺口及下一机命令。 |
| `server-integration-notes.md` | `ab90594cfb2faffbd600358b670a17666eb50e674dc926fcb798402c21e35dff` | 服务器技术交接说明；不是已实施或已部署回执。 |
| `server-existing-map-changes.patch` | `471b980c793c5ce6ae73a9a9b6b451a65d42cef3b66132e768f200cea1159650` | 仅迁移绑定26ceb70ca基线的预存工作；先apply --check。 |
| `server-existing-world-expansion-speed2x-20261101.json` | `f07368f555caee97863f342acb41f91b4ef0914061d8af27c206c613ac266e76` | 原未跟踪JSON的原字节副本；迁移至服务器原相对路径前后核SHA。 |

同目录另有 `verify_server_map_handoff.py`；以实际发布的脚本及其帮助/入口执行便携核验，不编造参数，不把读取帮助当校验通过。该脚本不表示服务器场景已注册。

## 实际依据入口

- 客户端：`README.md`、`Docs/QdaoMapDeliveries20261102.md`、`Docs/VerificationEvidence/festival-maps-20261106/README.md`、`Docs/VerificationEvidence/festival-maps-20261107/README.md`、同目录 `server-handoff-data.json`，以及 `Assets/Resources/World/QdaoMaps20261102/catalog.json`。
- 客户端门禁：`Assets/Scripts/World/CurrentMaps/CurrentMapCatalog.cs` 的 `CanTravelOnline`；地图生命周期及外观切换：`Assets/Scripts/World/Tianyong/TianyongMapRuntime.cs`。
- 服务器：`AGENTS.md`、`data/AGENTS.md`、`proto/scene/scene_info.proto`、`data/schema/basescene_table.proto`、`data/schema/world_table.proto`、`data/BaseScene.xlsx`、`data/World.xlsx`、`tools/data_table_exporter/exporter_config.yaml`，以及 `cpp/libs/services/scene/spatial/system/nav_query.h`。

本文件不附账户、密钥或机器私有配置；服务器数据/协议/导航代码细节以同批技术交接和实际仓库源文件为准。
