# 豆包客户端交接：26000／26008手绘NavMesh进入真实游戏

交接日期：2026-11-12。客户端工作目录：`D:/luyuan/mmorpg-client`。

用户已完成26000和26008的当前手绘范围，要求进入游戏看实际效果。**客户端代码接入、真实游戏验证与打包由豆包负责；原线程继续负责C++服务器。** 第七节已补充真实两图导出、共同出生点、配对收据及本地Docker部署PASS；客户端实际构建Player仍须完成接入和验证。

## 一、开始前必须保留的内容

- 先读 `D:/luyuan/mmorpg-client/AGENTS.md`、`D:/luyuan/mmorpg-client/Docs/NavMeshAuthoring.md` 和本文。不要reset/clean整个工程；当前存在其它窗口的协议、资源及客户端改动。
- 不重建、不覆盖用户草稿，不运行只适用于空白草稿的`PrepareFestivalDrafts.Run`。保留所有`.meta`／GUID和已有版本化NavMesh资产。
- 原交接时Unity与绘图窗口处于运行状态；本次方案审核时用户补充说明豆包尝试的代码已回滚、Unity进程已清零。实施前重新核实当前占用和保存状态，不控制桌面、不强退编辑器。修改源码会触发domain reload；有未完成轮廓时须先确认已经保存，不能用重新编译试探。保存工具已支持未完成轮廓暂存，烘焙仍拒绝待修复的自交或少于3点区域。
- 所有构建、日志和临时产物放本工程`Builds/`、`Logs/`、`.codex-artifacts/`或系统临时目录。禁止在`E:/work`顶层新增项目目录。
- 本交接只新增文档，没有修改客户端代码、草稿、运行中的Unity或正式资源登记。

## 二、已经画好的实际资产，不是空白画布

以下是交接时磁盘状态。用户仍可能继续修改，开始接入及出包前须重新读取并记录SHA，不能根据此表覆盖更新内容。

| 项目 | 26000中秋 | 26008春节 | 26008中秋 |
|---|---|---|---|
| `mapKey` | `26000-mid-autumn` | `26008-spring` | `26008-mid-autumn` |
| 草稿完整路径 | `D:/luyuan/mmorpg-client/Assets/MapNavigationDrafts/Festival20261111/26000-mid-autumn.asset` | `D:/luyuan/mmorpg-client/Assets/MapNavigationDrafts/Festival20261111/26008-spring.asset` | `D:/luyuan/mmorpg-client/Assets/MapNavigationDrafts/Festival20261111/26008-mid-autumn.asset` |
| 草稿GUID | `cba6b37b52a47cf4ca0424b245c6b51f` | `4bbc7c586cc5b2843ac3dc1fd1c98a0c` | `540b4dacdb502b94685dd77a2b50da7c` |
| 原画布／分块 | 5120×4096，5列×4行，20张1024²PNG | 4096×3072，4列×3行，12张1024²PNG | 4096×3072，4列×3行，12张1024²PNG |
| `worldRect` | `(x=0,z=0,width=300,depth=240)` | `(0,0,300,225)` | `(0,0,300,225)` |
| 已存区域 | 9区：1可走、8障碍 | 6区，全部可走 | 0区，尚未制作导航 |
| 各区顶点数，保持原顺序 | 88／113／23／51／18／30／13／13／5 | 26／15／16／8／60／24 | 无 |
| `spawnSet` | `true` | **`false`，不可把资产里的零值当出生点** | `false` |
| 已存Unity脚点 | `(110.92121,0,33.47244)` | 未设置 | 未设置 |
| `boundsConfirmed` | `false` | `false` | `false` |

当前两张已画地图的世界范围是**联调工作尺度**，尚未确认正式有效边界；不能自行改成旧地图尺度、强行套4:3或把原图统一改成2560×1080／1920×1080。26008的6块可走区域也不能为了连通而自动铺满背景空白。

### 已引用的烘焙结果

| 项目 | 26000中秋 | 26008春节 |
|---|---|---|
| NavMesh完整路径 | `D:/luyuan/mmorpg-client/Assets/MapNavigationDrafts/Festival20261111/26000-mid-autumn.navmesh-9d2185138bdcb22c.asset` | `D:/luyuan/mmorpg-client/Assets/MapNavigationDrafts/Festival20261111/26008-spring.navmesh-504096e2f15fb98b.asset` |
| NavMesh GUID／fileID | `c871785b2398fba4cbddeab29609e37a`／`23800000` | `1492049b46b65574c8f4500302a2a533`／`23800000` |
| 草稿已存`bakedGeometrySha256` | `9d2185138bdcb22cd7c296a028d28c93d24b8dd3b48ec96da36b1a7e16ac78d4` | `504096e2f15fb98b5de8e0e7a036c13c7d31a9e0adaa17b8927dc8607a3e6609` |
| 草稿文件SHA256 | `401ce609036473e50afc24c0e35990cde82ff80a8303e8e62f79645417f6fdf2` | `dd1d5f603bd3d1376b3a0631e923839e79477eaa85c3e37837f025223e706bf4` |

上述两份指纹现已通过本次隔离Unity的`NavMeshAuthoringAssetIO.IsBakeCurrent(draft)`及正式Export检查，原工程资产前后SHA锁定PASS，详见第七节delivery收据。豆包开始接入和出包前仍须检查是否出现后续变化；若用户改变了几何或合同导致失效，保留作者数据并重新烘焙，不重新描图。26008中秋草稿文件SHA256仍为`0848d836f642063db4c4cf9e8e94c93a6cb54df2a21222f9bc4d44a53fb1025c`。

26008春节已由原线程在隔离Unity中选择**联调用安全脚点`[122.5,0,127.5]`**并写入共同delivery，原草稿仍保持`spawnSet=false`，没有自动修改作者资产。豆包的客户端运行配置必须读取共同delivery中的`spawnUnity`，不能各自选点或把它回写成作者已设置的出生点。详情及manifest路径见第七节；这项离线验证不代表联机入口已完成。

## 三、图片、导航与坐标合同

原图逐片引用目录：

- `D:/luyuan/mmorpg-client/Assets/Resources/World/FestivalPreview20261111/26000/mid-autumn/`
- `D:/luyuan/mmorpg-client/Assets/Resources/World/FestivalPreview20261111/26008/spring/`
- `D:/luyuan/mmorpg-client/Assets/Resources/World/FestivalPreview20261111/26008/mid-autumn/`

文件名是`r01_c01.png`等，行自北向南、列自西向东。草稿`backgroundTiles`逐片保存资源路径、原始像素尺寸和SHA256；这里没有总览图替代高清图块。26008春节沿用既定的四片喷泉替换结果，不重新抽选来源。图片显示与导航几何分开：PNG只显示景物，作者多边形才决定可走范围。

`D:/luyuan/mmorpg-client/Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs`的转换为：

```text
世界X = worldRect.xMin + pixelX / imageWidth * worldRect.width
世界Z = worldRect.yMax - pixelY / imageHeight * worldRect.height
世界Y = 0
```

多边形点是原图左上原点的像素坐标；`spawn`的Vector2保存的是**世界X/Z**，不是像素坐标。`NavigationPolygonGeometry.Build`合并可走区、扣除障碍并生成+Y法线的平面Mesh。不得从背景图片平板烘焙成全图可走，也不需要建立完整三维建筑模型。

两张已画草稿当前参数一致：`agentTypeId=0`、半径`.4`、高度`1.8`、台阶`.3`、坡度`45°`、水平体素`.1`。当前显式合同`authored-flat-v2`：tile为256体素、minRegionArea为0，Unity开启`buildHeightMesh`，自动跳跃和落差连接为0；脚点查询按X/Y/Z每轴`.02`比较。半径等来自草稿的局部BuildSettings，**不能直接用全局Humanoid默认半径.5、高2、台阶.75替代**。

Unity HeightMesh的`SamplePosition`和实际角色脚点应保留Y=0；原始`CalculatePath().corners`／`CalculateTriangulation`仍可能是近似网格Y=.05。不要将这些原始拐点高度写回人物，也不要把拐点／三角形高度逐位相同写成验收结果。

网络换轴继续只走`D:/luyuan/mmorpg-client/Assets/Scripts/World/WorldCoordinateConverter.cs`：

```text
Unity(x,y,z) → 服务器(X=z,Y=x,Z=y)
服务器(X,Y,Z) → Unity(x=Y,y=Z,z=X)
```

26000现有Unity出生点对应服务器轴为约`(33.47244,110.92121,0)`。**OBJ本身保持Unity Y-up／X-Z地面，不能再按网络轴换一次。** Unity NavMeshData不是服务端UE5 double ABI的Detour MSET，两个文件不可互相替换。

## 四、目前“进游戏”尚未连接这两份NavMesh

### 正式入口与旧路径

当前`D:/luyuan/mmorpg-client/ProjectSettings/EditorBuildSettings.asset`启用：

1. `D:/luyuan/mmorpg-client/Assets/Scenes/Bootstrap.unity`，GUID`26e024c5553180c4092e33083d99e6aa`。
2. `D:/luyuan/mmorpg-client/Assets/Scenes/World/TianyongSandbox.unity`，GUID`57a6420b2aba63e459a32dce4e9e2873`。

正式uGUI入口`D:/luyuan/mmorpg-client/Assets/Scripts/UI/AppBootstrap.cs`创建`GameClient`、设置ActorWorld根节点，并取得或添加`TianyongMapRuntime`，调用`WorldMap.Initialize(GameClient, worldCamera, directionalSun, mapConfig)`。

真实协议地图入口在`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongMapRuntime.cs`：订阅`GameClient.OnSceneEntered`／`World.OnLocalPlayerChanged`，按`SceneInfoComp.SceneConfigId`查询`CurrentMapCatalog`，由`TryInstallMap`构建、绑定本地角色、设置相机、卸载旧图。客户端游戏连接类在`D:/luyuan/mmorpg-client/Assets/Scripts/Game/GameClient.cs`。`AttachLocalController`及`ReportRelocatedPlayer`也需纳入新导航接入，否则入场后的旧mask恢复逻辑可能将角色重新拉回旧网格。

现有正式catalog是`D:/luyuan/mmorpg-client/Assets/Resources/World/QdaoMaps20261102/catalog.json`，Resources入口为`World/QdaoMaps20261102/catalog`。其中目前登记21003、28007、35004、1001、1002、5008、10000、19004、23006，**没有26000／26008**，且现有交付均`clientOnly=true`。旧主城／八仙岛／东海渔村另保留sceneConfigId 1／2／3。不要把26000或26008偷偷映射成某张旧地图编号。

以下接口仍是旧位图导航／A*：

- `D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapCatalog.cs`：`CurrentMapDefinition.CreateNavigation()`返回`TianyongNavigationGrid`；`Data`要求`source.json`和Base64 `walkmask`，现代交付还要求`tiles.json`。
- `D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapBuilder.cs`：构建旧`TianyongMapInstance`及mask导航。
- `D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongPlayerController.cs`：`Initialize`、地图绑定、`PlanPath→_navigation.FindPath`、WASD步进／服务器纠偏后的`IsWalkable`检查均强依赖`TianyongNavigationGrid`；移动由CharacterController驱动，并保留MoveStart／MoveSync／MoveStop、输入门禁、队伍服务器跟随等逻辑。
- `D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/FestivalMapLocalTestPolicy.cs`：本机测试白名单也没有26000／26008；不能只加一个UI按钮就绕过catalog／旅行／场景登记。

不能只把两份NavMesh放进工程后宣称真实游戏已使用。需要给这两张图增加可区分的NavMesh运行定义／场景映射，并让实际本地人物的寻路、步进、出生、恢复和网络位置上报统一使用它；保留其它旧地图已有行为。

### 两种现成预览各自证明什么

- `D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/FestivalMapVisualPreview.cs`及`Assets/Resources/World/FestivalPreview20261111/preview-catalog.json`只有三套看图入口。打开时会停用旧人物控制器、切换预览相机；这里没有接入导航或联机传送。
- `D:/luyuan/mmorpg-client/Assets/Scenes/World/FestivalMaps.unity`（GUID`bdbd1a2380e7457683e44b0c546a4021`）及`TianyongSandboxBootstrap`上的26000／26008按钮仍调用该视觉预览。沙盒原有可玩地图列表是21003／28007／35004。
- `D:/luyuan/mmorpg-client/Assets/Scripts/World/NavigationAuthoring/NavMeshDraftPreview.cs`是独立草稿试走，使用真实NavMeshAgent，但私有快照、独立场景、合成标记角色都不等于正式登录角色接入。
- `D:/luyuan/mmorpg-client/Assets/Editor/FestivalVisualPreviewBuild.cs`明确只构建离线视觉包。**不要用它打出的FestivalMaps.exe替代用户要求的真实游戏试走客户端。**

## 五、豆包要交付的客户端改动

1. 以26000中秋和26008春节这两个**已画NavMesh**作为运行导航源；先核实指纹与出生点，再建立客户端可加载的资源映射。草稿和NavMesh现在位于`Resources`外，Editor的AssetDatabase路径不能直接用于Player；可用正式运行资产通过序列化引用持有NavMeshData／图块，或现有资源交付机制。不能手改`.asset`／GUID来凑引用。
2. 为真实场景入场和退出接入地图生命周期：按最终服务端sceneConfigId+appearance解析，共享同一份worldRect／测试spawn；加载NavMeshData、原图块和角色后提交新图，切图失败保留合理旧状态，切图／断线时移除自己持有的NavMeshData实例与资源，避免双图全局查询污染。
3. 真实人物点击移动仅接受完整可达路径；不可达断岛不能把partial路径宣称成功。WASD及连续步进必须受NavMesh约束，不能沿用旧mask拦截新图、也不能打开debugIgnoreMask绕过障碍。可复用现有控制器并引入导航抽象；不要让NavMeshAgent和非运动学Rigidbody／CharacterController同时争抢transform。
4. 保留当前实际游戏的姓名、角色外观、方向动画、输入门禁、点击标记、队伍服务器跟随、速度和MoveStart／MoveSync／MoveStop协议行为。脚点上报继续经`WorldCoordinateConverter`；角色高度以源地面查询为准，不靠整体抬高.2修正。现有`FeetPosition`由`transform.TransformPoint(motor.center + Vector3.down * motor.height * .5)`取得，不能将胶囊中心当成脚点。现有motor尺寸来自`TianyongMapConfig.PlayerHeight／PlayerRadius／PlayerStepOffset`，接这两张图时须与草稿的`.4／1.8／.3`合同明确映射；仅设置NavMeshAgent类型ID不会自动完成映射。
5. 图片仍逐块显示原PNG；使用现有角色跟随与滚轮缩放习惯。`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongCameraController.cs`已有俯视、SetTarget、SetWorldBounds、平滑和边缘约束，可沿用。26000按300×240、26008按300×225设置本轮联调边界，不能沿用旧世界矩形或仅把整图静态塞满屏幕。
6. 提供能从**真实游戏会话**进入两张图的联调入口，并显示地图身份／appearance以免混图。其场景ID及门禁必须与原线程最终服务器收据一致；联机入口未就绪时明确报告阻塞，不用草稿Play或纯看图来冒充。
7. 26008中秋仍为空：本次不自动复用春节NavMesh，不宣称已完成；若未来要共用，必须验证两套地面／障碍同源和状态依据。

## 六、验证、构建与回执

已有工具链与合成验证文档：`D:/luyuan/mmorpg-client/tools/tests/navmesh_authoring/README.md`。可在不关闭当前主Unity的情况下运行隔离工具回归：

```powershell
pwsh -File 'D:/luyuan/mmorpg-client/tools/tests/navmesh_authoring/run_verification.ps1' `
  -UnityPath 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe' `
  -OutputDirectory 'D:/luyuan/mmorpg-client/.codex-artifacts/doubao-navmesh-tool-verification'
```

这类合成检查只证明工具链。豆包还必须对**当前两张真实资产和实际构建Player**检查：实际入场脚点、点击绕障、WASD边缘、窄路／桥、断岛拒绝、相机跟随与变焦边界、全图块清晰度与接缝、两图反复切换、重新登录、联机移动回执及服务器是否持续回拉。至少记录代码版本、草稿SHA、NavMesh GUID／指纹、场景映射和服务器manifest／MSET SHA。

Unity固定版本是`6000.6.0f1`；本机路径`D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe`。官方AI Navigation是2.0.16，项目已经安装；本次无需迁移NavMeshSurface。客户端本轮改动由豆包完成后，可使用实际主场景构建脚本：

```powershell
pwsh -File 'D:/luyuan/mmorpg-client/tools/build_client.ps1' `
  -UnityExe 'D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe' `
  -OutDir 'D:/luyuan/mmorpg-client/Builds/festival-authored-navmesh-20261112-r1' -Zip
```

`OutDir`必须是不存在的新目录；该脚本按当前已启用场景构建，产物是`mmorpg.exe`、`mmorpg_Data`、`UnityPlayer.dll`及可选ZIP，日志位于`Logs/`。脚本不能同时打开同一工程做batch构建；先让用户保存当前工作并协调编辑器占用，或使用符合根AGENTS的独立构建副本，不强退当前Unity。`tools/build_crosszone_player.ps1`面向跨区验证，默认Unity路径还指向旧版本，不是本次首选。

交付回执应包含：构建目录／ZIP／exe SHA、实际打包场景、代码版本、两份运行NavMesh与作者草稿的对应关系、26008联调spawn来源、入场及移动测试结果、服务器版本和manifest／MSET SHA、所有未完成项。截图标注来源是发布Player还是Editor；不能把历史预览图、切片哈希或编译通过单独当作当前游戏验收。

## 七、已确认的真实地图交付与配对收据

本轮已在隔离Unity中读取两份实际用户草稿、原GUID和当前NavMesh，完成原图SHA、同源几何、当前烘焙指纹、出生点和正式Export检查；源工程资产／PNG／NavMesh及meta前后SHA锁定PASS。两张**真实用户地图**合计69个点和12条路线的Unity／重新加载MSET配对检查PASS。这是有限样本的离线配对结果，不代表全图每点、正式登录或联机移动已验收。

配对工具的`scope`字段沿用了工具内固定的`Synthetic`文案；本轮实际输入身份以共同delivery绑定的两份作者资产SHA、mapKey、OBJ及MSET SHA为准。这里保留原报告字节和SHA，不改写该字段来包装验收结论。

共同manifest原路径：

`D:/luyuan/mmorpg-client/.codex-artifacts/authored-festival-export-20261112/delivery/delivery.json`

服务器配对报告原路径：

`D:/luyuan/mmorpg-client/.codex-artifacts/authored-festival-export-20261112/paired-server/paired-verification.json`

稳定证据副本已按原字节保存到：

- [delivery.json](../VerificationEvidence/authored-festival-delivery-20261112/delivery.json)，完整路径`D:/luyuan/mmorpg-client/Docs/VerificationEvidence/authored-festival-delivery-20261112/delivery.json`，SHA256 `f5cc6fa645f79180b5d1a3a71f279004bc6dbb5101c8c05e15a028de40bd3f79`。
- [paired-unity-queries.json](../VerificationEvidence/authored-festival-delivery-20261112/paired-unity-queries.json)，为实际两图Unity查询原始收据，SHA256 `fb57f7843e000cbad4567733e0549e81f29d7db64e0baf679f68aee3c10c659e`。
- [paired-verification.json](../VerificationEvidence/authored-festival-delivery-20261112/paired-verification.json)，完整路径`D:/luyuan/mmorpg-client/Docs/VerificationEvidence/authored-festival-delivery-20261112/paired-verification.json`，SHA256 `f5d962ce762b5af1065c5393dffb8a398bcf5c6115c0a9bf03856f2a8d1769cf`。

delivery中的`exportDirectory`指向每图真实OBJ、`navigation.json`、`source.json`、`server-bake-args.json`及Unity查询。导入器以每图`source.json`绑定的实际文件SHA和`navigation.json`的坐标／参数为权威交叉核对，不能手写一份近似参数替代。稳定副本没有把原导出目录改写成另一个位置；复制完整交付目录时须一并保留其内容和SHA。

| 已确认映射 | 26000中秋 | 26008春节 |
|---|---|---|
| `sceneConfigId` | `26000` | `26008` |
| `mapKey`／appearance | `26000-mid-autumn`／`mid-autumn` | `26008-spring`／`spring` |
| 共同`spawnUnity` | `[110.921211,0,33.47244]` | `[122.5,0,127.5]` |
| 服务器坐标 | `[33.47244,110.921211,0]` | `[127.5,122.5,0]` |
| 出生点依据 | 保留作者已存出生点 | 仅Instantiate副本选安全点，原草稿`spawnSet=false`不变 |
| 草稿SHA256，来自receipt | `401ce609036473e50afc24c0e35990cde82ff80a8303e8e62f79645417f6fdf2` | `dd1d5f603bd3d1376b3a0631e923839e79477eaa85c3e37837f025223e706bf4` |
| 当前作者几何指纹，来自receipt | `9d2185138bdcb22cd7c296a028d28c93d24b8dd3b48ec96da36b1a7e16ac78d4` | `504096e2f15fb98b5de8e0e7a036c13c7d31a9e0adaa17b8927dc8607a3e6609` |
| 服务器目标MSET相对路径 | `data/scene_nav_bin/authored_maps/26000/scene.bin` | `data/scene_nav_bin/authored_maps/26008/scene.bin` |

两个worldRect与`boundsConfirmed=false`仍按前表保留，运行配置不得因此自动标记为正式有效范围已确认。

### 本轮服务器环境及本地部署结果

Canonical Docker目标为 **`xuanming-debug-current`**，gateway端口**18085**，gate端口**28000**。这套手绘导航走C++服务器的development-only场景表overlay，不修改正式源BaseScene／World。**同级旧Java完整服务器尚未接入本次ANAV导航合同。当前Canonical栈的gateway本身由本C++仓库的`java/gateway_node`实现，它属于本轮联调环境并继续复用。** 客户端按项目身份及18085／28000端口选择这套入口，不要连到同级旧Java完整服务器的其它入口；不能只凭网关实现语言判断目标。

**本地Docker部署已完成并通过最终门禁**，原始收据目录为`D:/luyuan/xuanming-server-mmo-cpp/build/verification/authored-festival-20261112/deployment-20261112-r2`，`result.json`为`DEPLOYED_AND_VERIFIED`。gate／scene／battle使用同批镜像`local/xuanming-node:debug-20261112t140808.551745z`，imageId为`sha256:bfb8190e6ec06cfb9a4f14e1899868b55bec9d0f843cae119858a8262d342804`；Go三个表消费者和当前网关继续复用原镜像。

新scene容器自己的启动日志已确认登记两图，三C++节点只读导航挂载的16个文件SHA、六个消费者的新场景表，以及当前源码／实际二进制门禁均PASS。两份运行MSET SHA分别为26000的`889942f36dd8c272cd4688070a279119709069aa3896bc4e6fa6e6cafcd8ee1f`、26008的`79673906ee2ce6a47fe7fc51488b5ba9d0e538e0b2d85b240cb88ce15de9557e`；它们与配对报告一致。未重建数据库、清卷或修改既有账号；其它服务容器及历史迁移容器身份保持。

开发overlay文件为`D:/luyuan/xuanming-server-mmo-cpp/build/verification/authored-festival-20261112/compose.authored.json`，来源清单为同目录`runtime-data-manifest.json`。以后用Compose重新创建这六个消费者时须保留这份overlay；只使用原两份Compose文件会移除本次开发导航／场景表挂载。复查用`tools/scripts/rebuild_docker_debug.py check --services gate,scene,battle,login,scene-manager,player-locator --runtime-data-manifest build/verification/authored-festival-20261112/runtime-data-manifest.json`，从C++服务端根目录运行，不绕过原源码和二进制检查。

稳定证据副本位于`D:/luyuan/mmorpg-client/Docs/VerificationEvidence/authored-festival-delivery-20261112/`：`server-docker-build-result.json`、`server-deployment-result.json`、`server-runtime-check.json`、`server-scene-registration.log`、`server-scene-log-provenance.json`、`server-runtime-data-manifest.json`及`server-native-summary.json`。原生24项回归、23行实际装载和旧导航9项负对照也已保存。私有环境文件和登录凭据不属于交接材料。

**后端真实协议探针已通过**。原始报告为`D:/luyuan/xuanming-server-mmo-cpp/build/verification/authored-nav-e2e-20261112/actual-e2e-r2.json`，稳定副本为`D:/luyuan/mmorpg-client/Docs/VerificationEvidence/authored-festival-delivery-20261112/server-network-e2e.json`。测试在现有18085网关完成HTTP认证、连接28000 gate，并为两图各使用独立A／B测试角色；实际收到目标场景`NotifyEnterScene`与人物创建消息，两图出生脚点分别等于共同manifest，Y=0。

每图1米合法移动均无MoveAck纠偏，B观察者确认了A的服务器停步位置；随后按250ms、至多3m/s接近真实网格内部障碍并尝试穿越，每图均收到1次纠偏且停在原网格合法一侧，回执高度仍为0。完整探针44.22秒PASS。当前锁定采样中没有已证实的断岛落点，真实两图的断岛用例明确为`NOT_APPLICABLE`；不能把原生断岛测试扩张为这两图已经逐岛联机验证。

首轮旧robot认证夹具在登录时被拒绝，未入图／移动，原字节保存在`server-network-auth-fixture-failure.json`；仅修本次独立探针以复用当前HTTP及受控开发认证策略后，r2通过。未放宽服务器认证、未修改任何服务端凭据。后端探针没有Unity角色、碰撞体或相机，因此仍不能替代豆包的实际构建Player脚点、操作及画面验收。

豆包仍须完成第四、五节的**正式catalog或新NavMesh运行登记、TianyongMapRuntime、真实角色控制器及入场入口**接入，再构建客户端并做真实会话验证。仅拿现有代码重新打包不会自动让26000／26008进入游戏。

**可直接转发给豆包：服务端已部署并通过两图真实入场／移动／障碍拦截检查，可以按第八节审核结论完成客户端运行接入并打包。连接Canonical gateway18085／gate28000；只接26000中秋和26008春节，使用本节共同manifest及spawn。出包前仍须验证实际Player脚点、反复切图与正常操作，26008中秋不做。**

## 八、豆包八点方案审核结论与实施约束

**审核结论：保留“公共导航接口＋旧grid实现＋新NavMesh实现”的方向，原方案须按本节补齐后实施；不能只改十处类型签名就直接打包。** 本节依据当前实际源码审查，尚未实现客户端改造；客户端实施和打包仍由豆包负责。文中行号以本次核查版本为准，改动后须重新核对。

### 8.1 五方法接口缺连续移动段查询，矩形Clamp不是导航约束

原提案的五个方法为`IsWalkable`、`TryFindNearestWalkable`、`ClampXZ`、`FindPath`、`ResolveFallbackSpawn`。保留这些兼容入口可以，但不足以表达“从当前脚点到下一脚点的整段移动是否可行”。

- 在`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongPlayerController.cs:598`，当前只检查`candidateFeet`；`:610`移动后又在`:618`只检查最终脚点。两个端点都可走不能证明中间未穿过窄障碍、洞、断开的岛或地图边界。
- 新接口须增加明确的连续段能力，例如`Raycast`／`TryConstrainMove`，并规定命中点、是否阻挡、失败时是否允许移动。新NavMesh实现使用带当前filter的`NavMesh.Raycast`，同时严格验证起终脚点；CharacterController滑动后的实际位移也须复核，不把非法结果发到服务器。
- 旧grid可继续采用自身既有网格语义，不将NavMesh的半径侵蚀或新容差强加给旧图；原grid已有`HasLineOfSight`在`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongNavigationGrid.cs:278`，但它含路径平滑用的侧向余量，不能不加区分地套成所有旧图每帧步进的新规则。
- `ClampXZ`的`margin=2f`不是漏写了参数，实际旧实现见同文件`:336`，它只收缩矩形`EffectiveBounds`，不会把点变为可走点。控制器在`:418`及`:597`仍传2单位margin；新图已按`.4`角色半径烘焙，不能悄悄叠加旧的2单位矩形退让而裁掉作者可走边缘。保留旧图默认行为，并显式确定新图调用策略；墙内点击不可靠Clamp静默变成另一个目标。

### 8.2 严格合法判定统一每轴2cm，不接受`.12`容差

提案将普通`IsWalkable`放宽到`.12`、出生点单独`.02`，会让连续移动和目标点击接受服务器原脚点探针拒绝的点，必须修改。

- 所有authored-flat的合法脚点、点击目标、恢复后落点、每帧实际脚点都使用同一`MatchesGroundPoint`，实现已存在于`D:/luyuan/mmorpg-client/Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs:59`：X/Y/Z每轴差值均不超过`.02`。这不是欧氏距离`.02`，也不随体素变粗而放宽。
- `SamplePosition`的搜索半径与合法容差是两个参数；附近找到网格不等于请求点合法。先检查有限坐标和严格原脚点，再检查完整路径／连续段，不能用`CalculatePath`成功反过来证明墙内目标有效。
- `TryFindNearestWalkable`仅用于明确的恢复流程；须限制搜索、确认当前地图身份和允许的落点，不能把普通点击或每帧合法性检查偷换成“吸到附近另一岛”。恢复最终仍要通过严格脚点检查，并保留诊断，不能借客户端反复纠偏掩盖服务器／客户端版本不一致。

### 8.3 完整路径必须拒绝并反馈；压Y只限本次水平地面

`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongPlayerController.cs:414`的`PlanPath`和`:428`的空路径停步逻辑可保留，但要补齐以下语义：

- 新实现先严格检查两个端点，再调用带filter的`CalculatePath`，仅`PathComplete`可成为移动计划。`Partial`和`Invalid`不得取末点当成用户目标、画成功标记或继续旧路径。
- 返回空列表可以保留为兼容结果，但还必须向调用方传播失败原因，供UI显示“目标不可走／不连通／地图尚未就绪”；不要只停下不反馈。建议提供显式成功／失败结果或Try方法，避免依赖共享provider的可变“最后一次错误”。旧接口可包装到明确结果，接口方法数不必固定为五个。
- 本次两图是`authored-flat`、源地面Y=0。Unity raw corners可为Y=.05，运行移动可根据已验证的水平地面合同取得脚点Y=0；**不能把所有corners统一压成起点Y**，起点可能已受旧碰撞地板或异常服务器点影响而为-.05。
- 未来桥面、多层、台阶和斜坡须使用NavMesh高度采样或对应地形合同，不能沿用本次水平压Y规则。本次美术图中的桥没有被当作多层3D几何，不能据图片看起来有桥就宣称已有多层支持。

### 8.4 NavMesh所有权与事务切图必须重做，不能在构造时立即全局注册

`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongMapBuilder.cs:10`的`TianyongMapInstance`已经是地图资源所有者；其`:65`的`Dispose`目前只处理图块、Root和材质，尚不拥有NavMesh实例。原提案`NavMeshMapNavigation`构造函数直接`AddNavMeshData`与现有事务预构建方式不相容。

- `IMapNavigation`可以继承`IDisposable`，也可以将查询接口与拥有资源的具体对象分开；无论选择哪种，**地图instance唯一拥有native实例，Controller仅借用，不Dispose共享导航**。释放必须幂等，断线／切图／失败回滚都要立即移除本地图持有的`NavMeshDataInstance`，不能只等GameObject延迟销毁。
- 两图都使用`agentTypeId=0`且世界范围重叠。`NavMeshQueryFilter.agentTypeID`区分的是角色类型，不是地图；它不能将两份同类型数据隔离。inactive Root也不会自动撤销全局`NavMesh.AddNavMeshData`。
- `D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongMapRuntime.cs:168`先Build候选图，直到`:225`才Dispose旧图；若构造时Add，新旧网格会同时参与`:177`的出生点等查询。必须将“准备资源”与“激活全局导航”拆开。
- 标准顺序：准备Render／纹理／NavMeshData引用／元数据但不Add；捕获旧controller、camera及网络移动状态；在commit阶段短暂冻结输入与移动上报，撤旧Nav实例，再Add新Nav并严格校验共同spawn和目标地图数据；成功后提交controller／camera／map身份，最后释放旧资源并恢复输入。需要回滚时Remove新实例、重新激活旧实例，再恢复借用引用；不得在两个实例都激活时靠filter做验收。
- **网络场景语义不同于视觉回滚**：同场景刷新失败可恢复旧地图运行；服务器`NotifyEnterScene`已经切到新场景后，旧画面最多作为视觉fallback，不能继续按旧地图坐标发送MoveStart／MoveSync／MoveStop。此时应保持移动禁用，等待正确地图重入／重连恢复。现有catch区分`preserveCurrentOnFailure`和真实场景变化（同文件`:192`至`:219`），不得为了看上去可玩而抹掉此区分。

### 8.5 静态fallback与grid专用调用者不能只做类型替换

- `D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongNavigationGrid.cs:136`的`ResolveFallbackSpawn`改成public以实现接口可以；其当前算法包含旧地图默认点和2单位Clamp，须保持旧grid语义，不能作为新NavMesh的通用实现。
- 控制器另有**静态包装**`D:/luyuan/mmorpg-client/Assets/Scripts/World/Tianyong/TianyongPlayerController.cs:274`，空navigation时回退`TianyongMapDefinition.DefaultSpawn`。它的签名、Initialize、`ApplyPreparedMapNavigation`、`CaptureMapBinding`闭包及沙盒／正式runtime调用必须一起核对；只公开grid内部方法不能消除此依赖。
- 新NavMesh provider构造／运行定义中须显式携带共同delivery的spawn。26008必须`[122.5,0,127.5]`，不可从原草稿`spawnSet=false`的零值、旧城默认点或每次重新计算的安全点兜底。配置无有效spawn应在准备阶段明确失败，不能安装半就绪地图后每帧抛异常。
- 不止“Controller十处”受影响。`D:/luyuan/mmorpg-client/Assets/Scripts/App/FestivalRegionAutoVerify.cs:277`至`:280`直接使用`nav.Width／Depth`；`:466`、`:539`、`:560`的探针／找路辅助参数仍是具体grid类型。`D:/luyuan/mmorpg-client/Assets/Tests/PlayMode/QdaoRosterSandboxPlayModeTests.cs:195`、`:833`及多处EditMode测试也保留grid签名和`CellSize`断言。
- 通用NavMesh接口不应伪造`Width／Depth／CellSize`。grid专用验证留在grid分支，或使用额外可选的GridMetrics；新NavMesh探针读取实际NavMesh／delivery查询样本。编译整个客户端和相关Editor／PlayMode程序集，并验证旧地图，不只编译新增类。

### 8.6 新开发overlay有独立合同，不能伪装为已有正式交付

- `D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapCatalog.cs:46`的`ApplyDeliveries`先调用`MapDeliveryCatalog.Validate`；`D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/MapDeliveryContract.cs:119`至`:137`强制正式appearance、规范resourceRoot和layoutSha。当前PNG／作者草稿路径不满足这份正式交付格式，不能把`clientOnly`随手设false或伪造layoutSha来过校验。
- 推荐在统一lookup中明确合并**仅Development环境且显式启用**的AuthoredNavMesh运行登记，保持旧正式catalog校验原样。可给`CurrentMapDefinition`增加经过验证的显式导航来源分支，或用等价的运行定义适配；先定义清楚`Spawn／WorldRect／EffectiveWorldRect／ContentIdentity／appearance／navigationKind`，不要让新分支再落入旧mask的`Data`getter。
- 同文件`:107`的`CreateNavigation`及`:125`至`:146`的`Data`读取仍强制source／walkmask／tiles。仅换CreateNavigation返回接口而让`definition.Spawn`／`WorldRect`继续访问旧Data，依旧会在入场时失败。
- `FestivalMapLocalTestPolicy`白名单只覆盖既定旧交付，不是本次新开发overlay的正确入口。新入口应明确约束26000中秋／26008春节、共同delivery版本及Canonical gateway18085／gate28000，不能为开两张图放开所有旧`clientOnly`地图。
- 真实调用链仍须贯通`AppBootstrap→TianyongMapRuntime.EnterScene→查询新定义→准备/提交地图→AttachLocalController`。服务端场景号与客户端本地地图Key不能相互冒充；26008中秋不在本轮完成范围。

### 8.7 PNG运行引用和TileStreaming必须选择真实可用的分支

- `D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapBuilder.cs:19`先`LoadTexture`，`:55`才配置图块；旧`CurrentMapTileStreaming.Configure`在`D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapTileStreaming.cs:90`通过Resources读取`tiles`，缺失则直接返回，已有manifest还要经过`CurrentMapTilesManifest.Parse`。仅把新PNG目录交给现有streamer不会自动显示。
- 本次32张PNG可以用独立Authored地图renderer按已有`backgroundTiles`行列／像素尺寸铺图，运行定义显式序列化引用纹理和NavMeshData；本轮不必为了复用旧streamer伪造不存在的overview／walkmask／tiles。若选择复用streamer，就必须实现并验证符合其现有校验的真实`tiles.json`／source适配，不能绕过校验后声称已沿用正式链路。
- 运行登记asset可以位于Resources并序列化引用Resources外的NavMeshData／作者资源，或采用项目已有可交付的资源机制。Editor `AssetDatabase.LoadAssetAtPath`不可留在Player路径；仅把`Assets/...`字符串写入JSON不能让Player加载外部资产。所有引用须在实际构建依赖中验证，不能复制后产生失效GUID。
- 新renderer继续使用5120×4096／4096×3072原始地图及300×240／300×225工作尺度，不重拉伸图片，不覆盖真实游戏来伪造运行结果。相机沿用角色跟随、边界和滚轮逻辑，与旧图分支分清资源生命周期。

### 8.8 真实脚点与单一运动权威是发布门禁

提案“corners压Y”不会自动解决实际人物高度。`D:/luyuan/mmorpg-client/Assets/Scripts/World/CurrentMaps/CurrentMapBuilder.cs:32`的旧碰撞地板中心Y=-.55、高1，顶面为-.05；`TianyongPlayerController.cs:610`仍施加重力驱动CharacterController。旧地板、skinWidth、胶囊中心与新NavMesh地面可能不一致。

- 新authored-flat builder和运动策略须对齐源地面Y=0；不要无条件继承旧地板-.05，也不要把起点-.05传播到所有路径。**仅把Collider顶面改成0仍不能未经实测保证skinWidth下的真实脚点在0。**
- CharacterController／NavMeshAgent／ActorWorld服务器驱动必须有清楚的唯一位置权威。采用现有CC跟路径时，不让Agent同时更新transform；采用Agent时，不让CC重力又驱动同一位置。保持队伍服务器跟随切入／退出与MoveStart／Sync／Stop语义。
- 上报取现有`FeetPosition`而不是胶囊中心，继续使用`WorldCoordinateConverter`。本次两个开发地图的实际构建Player在入场、连续步进、拐角、停步和纠偏后，脚点必须每轴`.02`内匹配合法源地面采样；失败必须在出包回执中报告，不能改宽容差。

### 按此顺序实施和验收

1. 先完成公共查询语义、grid兼容与开发运行登记，编译所有受影响程序集；真实资产／GUID／共同spawn保持不变。
2. 实现未激活资源准备、唯一Nav实例激活、失败回滚和服务器已切场景时的移动禁用；加入同类型双图重叠、连续切图、加载失败、断线和Dispose幂等回归。
3. 接入严格端点、连续段、完整路径、失败反馈及单一运动权威；覆盖端点合法但中间过洞、`.03`越界负例、断岛、原地面Y0、26008 manifest spawn及旧grid地图回归。
4. 通过真实Player资源引用、PNG分块／相机和实际脚点检查后，再进入Canonical实际会话做两图入场／连续移动／不回拉验证，最后打包并记录版本／资源／服务器收据。离线草稿Play、server robot或仅编译都不能替代豆包负责的真实Player验证。
