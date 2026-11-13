# 图片地图 NavMesh 编辑接续 — 2026-11-12

## 未完成轮廓保存修复（2026-11-12）

用户已开始实际绘制，勿再按初始空白资产重建。旧窗口的Enter因单个轮廓自交被拒绝，顶部保存又漏掉_pending，造成区域数不增加。现已安装修复：顶部“保存当前绘制”把全部原始待绘点（含不足三点/自交）追加为草稿区域并持久化；无效区标“待修复”，高亮冲突边，修正后才允许烘焙。独立多边形之间的重叠仍允许，重叠障碍按并集扣除。窗口待绘状态改为可序列化；支持Return/KeypadEnter，切图前暂存，同图重开不清空。用户明确说由其自行重开Unity，未替用户重启；旧版未落盘的临时线不能保证随此次首次重载恢复，不可谎称已捕获。后台反射捕获曾被Unity命名空间安全校验拒绝，未绕过。写回前核对主源码未被其他任务改动，保留原文件副本；安装过程中20个用户资产文件逐字节未变。隔离保存8项及导航23项检查通过；GUID扫描0错误、6个既有无关警告。持久证据见`Docs/VerificationEvidence/navmesh-save-fix-20261112/summary.json`。未提交/推送。

## 后续修复结果（2026-11-12）

用户已选择继续使用现有绘图工具，亲手绘制可走区和障碍，并授权优先修复服务端0.2高度差及两端参数不一致。本节为当前状态；后文“未解决”的高度记录保留为修复前历史。

- 已加入手绘平面合同`authored-flat-v2`：客户端显式开启官方HeightMesh，服务器新增仅接受水平共面OBJ的`--profile authored-flat`，恢复粗网格/细节网格到原始平面高度。OBJ、角色脚点、出生点和服务端2cm条件均未抬高或放宽。
- 半径、高度、台阶、坡度和水平体素取同一草稿；tile明确256、minRegionArea明确0，关闭客户端全局自动跨越设置。导出`server-bake-args.json`及SHA，服务器新增坡度和世界面积CLI。Unity未公开的cellHeight/内部过滤参数，以及服务器有效台阶量化仍如实单列，不声称两套引擎所有内部算法相同。
- 独立审查补出旧客户端水平吸附容差漂移：默认5cm而服务器每轴2cm。现出生点、编辑器试寻路及Play统一调用`MatchesGroundPoint`按XYZ每轴2cm判定。生产入口3cm外边缘回归先红后绿。
- 隔离Unity6000.6.0f1实际源码/Play验证22项通过；服务器Release与28项回归通过；同源OBJ→真实落盘MSET重新加载的6组合成例中，36个点和6条路径/直线阻挡配对通过。覆盖洞边、外边缘、原脚点和+0.2负例、窄路、断岛、小岛、跨tile。
- 3类旧模式输出（旧mask、裁剪矩形mask含旁文件、普通OBJ）与原exe逐字节一致。常用`tools/navmesh_baker/build/Release/navmesh_baker.exe`和`navmesh_query.exe`已更新为本轮验证版本，旧exe保留。baker SHA256为`7d22adc031d97385d13cf3b76cd6fd701387a8566ded216cf2499fc4ff8e6fbd`，query为`363c8c4d1e687e63f9209a7f11d1899b9a625e0d683b848f953908e053fc093f`。
- 持久证据在`Docs/VerificationEvidence/navmesh-parity-20261112/summary.json`及同目录报告；实际编译/测试与来源哈希、红绿证据、旧模式字节比较均已保留。服务器原始构建日志位于其`build/verification/navmesh-authored-flat-20261112/`。

三份用户草稿及meta的SHA均与本轮开始相同，未重建或覆盖用户内容；没有接入Surface，没有更换正式MSET、场景mask/表或出生高度，没有控制用户Unity或运行服务，未提交/推送。旧烘焙数据因合同版本变化需由用户重新烘焙。

仍需区分：参数同源与配对用例通过不等于两种引擎轮廓逐位一致。合成洞左侧实际边界Unity为X=7.5，服务器为X=7.6；Unity原始路径拐点/三角网仍可Y=0.05，但HeightMesh查询和实际Agent脚点为Y=0。用户完成生产地图后，真实比例、边缘/通路、权威mask与有效范围及联机移动仍须逐图验证；不要把本次合成证据当作三套正式地图已完成导航。

## 接续目标

用户最新问题：**“NavMeshSurface 适合我们这个吗？上下文满了，交接给另外一个窗口继续。”**

交接创建过程中用户又补问：**“我们没有几何吧？”** 这是当前首先要回应的澄清：目前只有显示PNG的平面，三个草稿还没有绘制区域，因而没有专门的可走/障碍导航几何。不能向用户说成已有可直接烘焙的道路或建筑模型。用户圈出多边形后，`NavigationPolygonGeometry.Build` 才生成简化平面Mesh；不必建立完整三维城镇。Surface能消费该Mesh，但不能替代这一步，也不能从PNG自动识别图内障碍。是否接入Surface应按它对当前工作流的实际价值判断，不能仅因“官方”就认定可以直接自动出正确导航。

用户想为后续多张 PNG 分块拼成的地图亲手描绘可走地面/障碍，并使用 NavMesh；用户明确说服务端也用 NavMesh。先向用户讲清官方 NavMeshSurface 的适用条件，再基于现有工作提出最小接入方案。当前尚未实现 NavMeshSurface 组件接入；已有绘图工具和三份草稿均应继续使用。

建议判断：Surface 适合担当场景几何的收集/烘焙组件。多张贴图不影响；图片不会自动提供建筑/水域的障碍几何。可由当前多边形描绘生成可走 Mesh，再让 Surface 收集该 Mesh。仅将地图图片铺在平面上烘焙，会缺少图内建筑等阻挡信息。

官方资料：https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface.html 。页面说明 Surface 使用 Render Meshes 或 Physics Colliders 等场景几何，并由 Inspector Bake 生成 NavMeshData。

## 开始时的顺序

1. 读取客户端根 `AGENTS.md`，再读 `Docs/NavMeshAuthoring.md`。工作目录是 **D:/luyuan/mmorpg-client**；Unity **6000.6.0f1**，本机可执行文件 `D:/luyuan/tools/Unity/6000.6.0f1/Editor/Unity.exe`。
2. 查看当前工作树和三份草稿的实际内容；用户现在可能已经开始绘制。保留现有修改、GUID、草稿和正在运行的 Unity。用户明确要求 **不要使用 Computer Use**。
3. 检查当前 Bake 实现和官方 Surface API，明确向用户区分“官方组件/底层接口”和“项目自定义描绘窗口”。本轮任务是适用性评估与接入方案，尚没有实施 Surface 迁移。
4. 如后续实施 Surface：仅收集作者描绘生成的导航几何，使用专用层或 Current Object Hierarchy；避免把整个贴图底板一起纳入为全图可走。保持同一几何作为客户端烘焙和服务端 OBJ 的来源。
5. 处理角色参数来源：当前草稿直接覆盖 NavMeshBuildSettings 的半径、高度、台阶、坡度及体素；Surface 通常从 agentTypeID 对应官方配置读取设置。须明确映射，避免在用户不知情时修改全局角色类型，或让试走角色与烘焙参数不一致。

## 已有实现，勿误称为官方自带窗口

菜单：**MMORPG → Maps → NavMesh 导航编辑器**。

- 绘图窗口是本会话为项目编写的自定义 EditorWindow。
- 现在实际 Bake 调用的是官方 `UnityEngine.AI.NavMeshBuilder.BuildNavMeshData`；预览使用官方 `NavMeshData` / `NavMeshAgent`。
- 官方 AI Navigation **2.0.16** 已安装并在主 Unity 工程成功编译。依赖本地官方 tarball `tools/unity-packages/com.unity.ai.navigation-2.0.16.tgz`，SHA1 `d0cf77b009bbdba7a0bf95e0de8b25be0659b9a4`；`.gitignore` 已允许此包及来源说明纳入版本管理。
- 现有游戏旧地图代码中仍有 `TianyongNavigationGrid` 自定义 A*。本次工具没有自动把现有游戏移动/服务端流程全部迁移。用户要用 NavMesh，不要继续为本任务实现 A* 网格画刷。

关键源码：

- `Assets/Editor/NavigationAuthoring/NavMeshAuthoringWindow.cs`：整图/分块新建、多边形及矩形描绘、顶点编辑、撤销重做、出生点、路线检查、Play 场景、三套快捷按钮、1:1 查看。
- `Assets/Editor/NavigationAuthoring/NavMeshAuthoringAssetIO.cs`：导入、来源 SHA、直接官方 API 烘焙、同源 OBJ/JSON 导出。这里尚无 NavMeshSurface。
- `Assets/Editor/NavigationAuthoring/FestivalNavMeshDraftPresets.cs`：三个草稿的固定路径和快捷入口。
- `Assets/Scripts/World/NavigationAuthoring/NavMeshAuthoringDraft.cs`：ScriptableObject；多边形顶点是**底图像素坐标，左上原点**，世界地面为 Unity X/Z。
- `Assets/Scripts/World/NavigationAuthoring/NavigationPolygonGeometry.cs`：凹多边形三角化、可走区并集、障碍扣洞，生成 +Y 法线平面 Mesh。自交/过度复杂输入明确拒绝。
- `Assets/Scripts/World/NavigationAuthoring/NavMeshDraftPreview.cs`：私有运行时草稿快照、真实 NavMeshAgent、完整路径判定、WASD、镜头和资源释放。

## 三套已经导入的画布

目录：`Assets/MapNavigationDrafts/Festival20261111/`。

| 草稿 | 分块 | 原像素画布 |
|---|---|---|
| `26000-mid-autumn.asset` | 5列×4行，20张 | 5120×4096 |
| `26008-spring.asset` | 4列×3行，12张 | 4096×3072 |
| `26008-mid-autumn.asset` | 4列×3行，12张 | 4096×3072 |

窗口顶端可切换 **26000 中秋 / 26008 春节 / 26008 中秋**；首次空窗口默认26000。`spring` 表示春节。

创建时三份均为**零多边形、无出生点、无烘焙数据**，让用户亲手画。世界宽300、按图比例计算高度仅为作者工作尺度，`boundsConfirmed=false`。正式世界尺度/有效边界仍未知；尤其26000的5:4画布与旧4:3参考映射尚未证实。后续读实际资产，不能再按初始空白状态重建覆盖用户绘画。

脚本 GUID 为 `c3d9dbbdced62bc4fb209b55e2ae3162`，实际运行时程序集 **MmorpgClient**。三份资产已用同名程序集实际重载验证，`m_EditorClassIdentifier` 正确为 `MmorpgClient::MmorpgClient.World.NavigationAuthoring.NavMeshAuthoringDraft`；保持脚本/纹理 GUID。

## 图片来源与模糊问题

三套均逐片引用 `Assets/Resources/World/FestivalPreview20261111/26000/mid-autumn`、`26008/spring`、`26008/mid-autumn` 下的 `rNN_cNN.png`，不使用低清总览替代。

44张均1024×1024，实际 SHA 与指定 Azure image 根快照 `06ba7d8169f8d93173973a55b615998f4075564b` 的交接记录一致；26008春节使用清单选中的四块替换喷泉。原清单在 image 仓 `handoffs/machine-transfer-20261111-supplement/inventories/26000-formal.json` 和 `handoffs/machine-transfer-20261111/inventories/maps-a-current.json`。

保留主工程原 PNG `.meta` 的隔离 Unity 实际导入：44/44 都为 **1024×1024、RGB24、1 mip、Bilinear**，没有导入降采样。整幅5120/4096像素地图适配小窗口必然缩小；保存的 GameView 布局还记录了1931×814目标和约0.5706888视图缩放，这不是实时 GameView/DPI 测量。工具新增 **1:1 清晰查看**，已验证 `zoom * pixelsPerPoint == 1`；中键平移，适合窗口返回整图。

## 服务端接口与实际未解决项

实际 C++ NavMesh 服务端在 **D:/luyuan/xuanming-server-mmo-cpp**，不是旁边的 Java 仓。`tools/navmesh_baker/navmesh_baker.cpp` 接受 Unity **Y-up、X/Z地面** OBJ，内部不重复换轴，输出配套 UE5 `dtReal=double` Detour **MSET v1**。Unity NavMeshData `.asset` 与 MSET 不可直接互换。

现有 exe：`tools/navmesh_baker/build/Release/navmesh_baker.exe`。源码支持 `--obj/--out/--cs/--ch/--agent-radius/--agent-height/--agent-climb/--tile-size/--filter-ledges/--probe`；坡度固定50°，没有 slope CLI；`--ch` 当前固定0.2。源码与现成 exe 的版本完全匹配未独立证明。

真实隔离检查：同一个导出平面 OBJ（Y=0），出生探针 `(3,0,6)` 返回 **OFF MESH，exit1**；诊断探针 `(3,0.2,6)` 返回 ON MESH 并生成 MSET。这说明高度/查询投影仍需对齐，诊断不能冒充原脚点通过。原导出命令保留脚点Y=0以暴露差异。还应核对服务端的 nav_mask、有效范围和换轴适配。没有修改服务器正式导航/场景/出生高度，没有开放联机传送。

## 已验证的范围与证据

- `Docs/VerificationEvidence/navmesh-authoring-20261112/`：核心工具18项实际 Unity 隔离检查通过，包含几何/障碍/断岛/半径、Undo、SHA、私有快照及真实 Play NavMeshAgent 绕洞到达；其中截图为**合成测试图**，不是三套生产地图已完成导航的证据。
- `Docs/VerificationEvidence/festival-nav-drafts-20261112/`：真实三画布20/12/12来源、GPU导入、GUID/元数据、实际MmorpgClient程序集重载、窗口/快捷选择和1:1验证；草稿安装回执。
- 三草稿验证最后为 PASS，核验原文件SHA后才安装，后续仅精确规范化程序集标识行；未覆盖用户内容。
- 最近 meta/GUID 检查：21,205文件，0错误、6个旧的无关警告。主工程新窗口源码也已在用户运行的 Unity 中编译成功。
- 核心可复现检查：`tools/tests/navmesh_authoring/run_verification.ps1`；准备三草稿的 helper/说明也在该目录。最新 runner 已包含快捷 preset 源文件。

## 工作树与操作边界

本会话新代码、资源和依赖修改**尚未提交/推送**。客户端还存在其他窗口对 MatchService/BattleScript 协议等的改动；保留并核实，不 reset/clean 整个客户端。生成协议按根AGENTS的权威服务端/正式生成器规则处理。

image 仓此前已经用户授权重置为指定根快照并完整取 LFS。此接续无需再次清理共享 Git refs、HEAD 或图片，也无需重新下载44张。

下一窗口结束适用性评估时，应能清楚说明：当前已可画的内容是什么、Surface 将收集哪些几何、角色参数来自哪里、客户端与服务端分别输出什么，以及尚未实测通过的项目。将官方组件接入作为明确的后续改动范围，保留已完成画布与可追溯来源。
